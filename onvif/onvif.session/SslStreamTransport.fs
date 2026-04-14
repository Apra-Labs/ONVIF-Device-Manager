namespace odm.core

open System
open System.IO
open System.Net.Security
open System.Net.Sockets
open System.Security.Authentication
open System.ServiceModel
open System.ServiceModel.Channels
open System.Text
open System.Text.RegularExpressions
open System.Threading.Tasks

/// Raw TcpClient + SslStream transport for WCF.
/// gSOAP cameras stall when .NET's HttpWebRequest splits the TLS payload
/// across multiple records. This transport sends headers+body in a single write.
module internal SslStreamHelpers =

    let findCrLfCrLf (data: byte[]) =
        let last = data.Length - 4
        let mutable pos = -1
        let mutable i = 0
        while i <= last && pos < 0 do
            if data.[i] = 0x0Duy && data.[i+1] = 0x0Auy
               && data.[i+2] = 0x0Duy && data.[i+3] = 0x0Auy then
                pos <- i
            i <- i + 1
        pos

    let getHeaderValue (headerBlock: string) (name: string) =
        let prefix = name + ":"
        headerBlock.Split([| "\r\n" |], StringSplitOptions.None)
        |> Array.tryFind (fun l -> l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        |> Option.map (fun l -> l.Substring(prefix.Length).Trim())

    let stripDoctype (data: byte[]) =
        // Remove <!DOCTYPE ...> declarations to prevent XmlReader from loading external DTDs.
        // Some cameras (e.g. Milesight Analytics) include DOCTYPE in SOAP response bodies,
        // causing XmlReader to look for XMLSchema.dtd on disk and fail with FileNotFoundException.
        let xml = Encoding.UTF8.GetString(data)
        let cleaned = Regex.Replace(xml, @"<!DOCTYPE[^>]*(?:>|(?:\[.*?\]>))", "", RegexOptions.Singleline)
        if cleaned.Length = xml.Length then data  // no DOCTYPE found, return original bytes
        else Encoding.UTF8.GetBytes(cleaned)

    /// Decodes a complete chunked-encoded body buffer (operates on bytes already in memory).
    let decodeChunked (data: byte[]) =
        use ms = new MemoryStream()
        let mutable pos = 0
        let mutable proceed = true
        while proceed && pos < data.Length do
            let mutable eol = -1
            for i in pos .. data.Length - 2 do
                if eol < 0 && data.[i] = 0x0Duy && data.[i+1] = 0x0Auy then
                    eol <- i
            if eol < 0 then
                proceed <- false
            else
                let sizeStr = Encoding.ASCII.GetString(data, pos, eol - pos).Trim()
                let size = Convert.ToInt32(sizeStr, 16)
                if size = 0 then
                    proceed <- false
                else
                    let dataStart = eol + 2
                    if dataStart + size > data.Length then
                        failwithf "decodeChunked: chunk size %d exceeds buffer (dataStart=%d, bufLen=%d)" size dataStart data.Length
                    ms.Write(data, dataStart, size)
                    pos <- dataStart + size + 2
        ms.ToArray()

    type HttpResponse = {
        StatusCode: int
        ContentType: string
        Body: byte[]
    }

    /// Parses a complete HTTP response from a byte buffer (used when the full response
    /// has already been read into memory, e.g. in unit tests or Connection: close responses).
    let parseResponse (data: byte[]) =
        let sep = findCrLfCrLf data
        if sep < 0 then failwith "Invalid HTTP response: no header terminator found"
        let headerText = Encoding.ASCII.GetString(data, 0, sep)
        let bodyStart = sep + 4
        let rawBody = Array.sub data bodyStart (data.Length - bodyStart)

        let statusCode =
            let statusLine = headerText.Split([| "\r\n" |], StringSplitOptions.None).[0]
            Int32.Parse(statusLine.Split(' ').[1])

        let body =
            let raw =
                match getHeaderValue headerText "Transfer-Encoding" with
                | Some te when te.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0 ->
                    decodeChunked rawBody
                | _ -> rawBody
            stripDoctype raw

        let contentType =
            match getHeaderValue headerText "Content-Type" with
            | Some ct -> ct
            | None -> "application/soap+xml; charset=utf-8"

        { StatusCode = statusCode; ContentType = contentType; Body = body }

    /// Creates a new TLS connection to the given URI.
    let createConnection (uri: Uri) (timeoutMs: int) : TcpClient * SslStream =
        let host = uri.Host
        let port = if uri.IsDefaultPort then 443 else uri.Port
        let tcp = new TcpClient()
        tcp.Connect(host, port)
        tcp.ReceiveTimeout <- timeoutMs
        tcp.SendTimeout <- timeoutMs
        let ssl = new SslStream(tcp.GetStream(), false,
                      RemoteCertificateValidationCallback(fun _ _ _ _ -> true))
        // SslProtocols.Tls12 = 0xC00 = 3072; enum value exists at runtime on .NET 4.0+
        // but the named constant was added to the BCL metadata only in .NET 4.5.
        ssl.AuthenticateAsClient(host, null, enum<SslProtocols> 3072, false)
        (tcp, ssl)

    /// Sends an HTTP POST request over an existing SslStream (keep-alive).
    /// Headers and body are written in a single ssl.Write() call to avoid
    /// gSOAP cameras stalling on fragmented TLS records.
    let sendOnSslStream (ssl: SslStream) (uri: Uri) (bodyBytes: byte[]) (contentType: string) =
        let host = uri.Host
        let port = if uri.IsDefaultPort then 443 else uri.Port
        let hostHeader = if port = 443 then host else sprintf "%s:%d" host port
        let path = if String.IsNullOrEmpty(uri.PathAndQuery) then "/" else uri.PathAndQuery
        let headerStr =
            sprintf "POST %s HTTP/1.1\r\nHost: %s\r\nContent-Type: %s\r\nContent-Length: %d\r\nConnection: close\r\n\r\n"
                path hostHeader contentType bodyBytes.Length
        let headerBytes = Encoding.ASCII.GetBytes(headerStr)
        // Single ssl.Write() call: headers and body must arrive in one TLS record.
        let full = Array.zeroCreate (headerBytes.Length + bodyBytes.Length)
        Buffer.BlockCopy(headerBytes, 0, full, 0, headerBytes.Length)
        Buffer.BlockCopy(bodyBytes, 0, full, headerBytes.Length, bodyBytes.Length)
        ssl.Write(full)
        ssl.Flush()

    /// Reads an HTTP response from an open SslStream.
    /// Uses Content-Length or chunked Transfer-Encoding to read exactly the right
    /// number of bytes — does not rely on connection close to detect end of body,
    /// so the connection can be reused for subsequent requests.
    let readHttpResponse (ssl: SslStream) : HttpResponse =
        // Phase 1: accumulate bytes until we find the header terminator \r\n\r\n
        let accum = new MemoryStream()
        let readBuf = Array.zeroCreate<byte> 4096
        let mutable headerEnd = -1
        while headerEnd < 0 do
            let n = ssl.Read(readBuf, 0, readBuf.Length)
            if n <= 0 then raise (IOException("SSL connection closed before HTTP headers received"))
            accum.Write(readBuf, 0, n)
            let sep = findCrLfCrLf (accum.ToArray())
            if sep >= 0 then headerEnd <- sep

        let accumulated = accum.ToArray()
        let headerText = Encoding.ASCII.GetString(accumulated, 0, headerEnd)
        // Bytes past \r\n\r\n already read from the stream
        let preloaded = Array.sub accumulated (headerEnd + 4) (accumulated.Length - headerEnd - 4)

        let statusCode =
            let statusLine = headerText.Split([| "\r\n" |], StringSplitOptions.None).[0]
            Int32.Parse(statusLine.Split(' ').[1])

        // Phase 2: read body based on Content-Length or chunked encoding
        let isChunked =
            match getHeaderValue headerText "Transfer-Encoding" with
            | Some te -> te.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0
            | None -> false

        let rawBody =
            if isChunked then
                // Incrementally decode chunked body from the open stream.
                // Uses a ResizeArray as a sliding buffer; preloaded bytes seed it.
                let buffer = ResizeArray<byte>(preloaded)
                use result = new MemoryStream()

                let readMore () =
                    let tmp = Array.zeroCreate<byte> 4096
                    let n = ssl.Read(tmp, 0, tmp.Length)
                    if n > 0 then buffer.AddRange(Array.sub tmp 0 n)
                    n > 0

                // Read a CRLF-terminated line from buffer, fetching more bytes as needed
                let readLine () =
                    let mutable lineEnd = -1
                    while lineEnd < 0 do
                        for i in 0 .. buffer.Count - 2 do
                            if lineEnd < 0 && buffer.[i] = 0x0Duy && buffer.[i+1] = 0x0Auy then
                                lineEnd <- i
                        if lineEnd < 0 then
                            if not (readMore()) then raise (IOException("Connection closed reading chunked body"))
                    let line = Encoding.ASCII.GetString(buffer.ToArray(), 0, lineEnd)
                    buffer.RemoveRange(0, lineEnd + 2)
                    line

                // Read exactly n bytes from buffer, fetching more bytes as needed
                let readBytes (n: int) =
                    while buffer.Count < n do
                        if not (readMore()) then raise (IOException("Connection closed reading chunk data"))
                    let data = Array.sub (buffer.ToArray()) 0 n
                    buffer.RemoveRange(0, n)
                    data

                let mutable cont = true
                while cont do
                    let sizeLine = readLine().Trim()
                    // Ignore chunk extensions (after ';')
                    let size = Convert.ToInt32(sizeLine.Split(';').[0].Trim(), 16)
                    if size = 0 then
                        cont <- false
                        // Drain any trailing headers until the empty terminating line
                        let mutable draining = true
                        while draining do
                            if readLine() = "" then draining <- false
                    else
                        let chunkData = readBytes size
                        result.Write(chunkData, 0, chunkData.Length)
                        readBytes 2 |> ignore  // trailing CRLF after each chunk

                result.ToArray()
            else
                match getHeaderValue headerText "Content-Length" with
                | Some lenStr ->
                    let contentLen = Int32.Parse(lenStr.Trim())
                    if preloaded.Length >= contentLen then
                        Array.sub preloaded 0 contentLen
                    else
                        use ms = new MemoryStream(contentLen)
                        ms.Write(preloaded, 0, preloaded.Length)
                        let mutable remaining = contentLen - preloaded.Length
                        let tmp = Array.zeroCreate<byte> 4096
                        while remaining > 0 do
                            let toRead = min remaining tmp.Length
                            let n = ssl.Read(tmp, 0, toRead)
                            if n <= 0 then raise (IOException("Connection closed before body complete"))
                            ms.Write(tmp, 0, n)
                            remaining <- remaining - n
                        ms.ToArray()
                | None ->
                    // No Content-Length and not chunked: fall back to reading until close.
                    // This handles non-compliant responses; connection cannot be reused after this.
                    use ms = new MemoryStream()
                    ms.Write(preloaded, 0, preloaded.Length)
                    let tmp = Array.zeroCreate<byte> 4096
                    let mutable n = ssl.Read(tmp, 0, tmp.Length)
                    while n > 0 do
                        ms.Write(tmp, 0, n)
                        n <- ssl.Read(tmp, 0, tmp.Length)
                    ms.ToArray()

        let contentType =
            match getHeaderValue headerText "Content-Type" with
            | Some ct -> ct
            | None -> "application/soap+xml; charset=utf-8"

        { StatusCode = statusCode; ContentType = contentType; Body = stripDoctype rawBody }


/// WCF IRequestChannel that sends SOAP via raw TcpClient + SslStream.
type SslStreamRequestChannel(factory: ChannelManagerBase, encoder: MessageEncoder,
                              wsAddressing: bool, address: EndpointAddress, via: Uri) =
    inherit ChannelBase(factory)

    let bufMgr = BufferManager.CreateBufferManager(int64 (64 * 1024 * 1024), Int32.MaxValue)
    // ChannelParameterCollection is required by WCF for security token propagation
    // (NvtSession.SetupUserNameToken adds SecurityUserNameToken here, then
    // CustomBehavior.BeforeSendRequest reads it back).
    let channelParams = new ChannelParameterCollection()

    // Persistent SSL connection — reused across SOAP calls to avoid per-call TLS handshakes.
    let mutable persistentConn: (TcpClient * SslStream) option = None
    let connLock = obj()
    // Serializes the entire send/receive cycle so that concurrent BeginRequest calls
    // on the same channel do not race on the shared SslStream.
    let requestLock = obj()

    /// Returns the existing connection if the TCP socket still reports connected;
    /// otherwise disposes the stale connection and creates a fresh TLS session.
    let getOrCreateConnection (timeoutMs: int) =
        lock connLock (fun () ->
            match persistentConn with
            | Some (tcp, _) when tcp.Connected ->
                persistentConn.Value
            | Some (tcp, ssl) ->
                try (ssl :> IDisposable).Dispose() with _ -> ()
                try (tcp :> IDisposable).Dispose() with _ -> ()
                persistentConn <- None
                let conn = SslStreamHelpers.createConnection via timeoutMs
                persistentConn <- Some conn
                conn
            | None ->
                let conn = SslStreamHelpers.createConnection via timeoutMs
                persistentConn <- Some conn
                conn
        )

    /// Clears and disposes the stored persistent connection (called on I/O error).
    let clearConnection () =
        lock connLock (fun () ->
            match persistentConn with
            | Some (tcp, ssl) ->
                try (ssl :> IDisposable).Dispose() with _ -> ()
                try (tcp :> IDisposable).Dispose() with _ -> ()
                persistentConn <- None
            | None -> ()
        )

    static let completedAr (callback: AsyncCallback) (state: obj) : IAsyncResult =
        let tcs = new TaskCompletionSource<bool>(state)
        tcs.SetResult(true)
        if callback <> null then callback.Invoke(tcs.Task :> IAsyncResult)
        tcs.Task :> IAsyncResult

    member private this.RequestCore(message: Message, timeout: TimeSpan) =
        let timeoutMs = max (int timeout.TotalMilliseconds) 15000

        // SOAP 1.2: embed action in Content-Type
        let action = message.Headers.Action
        let contentType =
            if String.IsNullOrEmpty(action) then encoder.ContentType
            else sprintf "%s; action=\"%s\"" encoder.ContentType action

        // Remove Action mustUnderstand header before serialization (non-WS-Addressing channels only).
        // gSOAP camera firmware (2.8.x) returns HTTP 500 / MustUnderstand fault for any
        // WS-Addressing header it does not recognise.  WS-Addressing channels (Events/Metadata)
        // keep the header because cameras use it for operation dispatch.
        if not wsAddressing then
            let actionIdx =
                message.Headers
                |> Seq.tryFindIndex (fun h -> h.Name = "Action" && h.MustUnderstand)
            match actionIdx with
            | Some i -> message.Headers.RemoveAt(i)
            | None -> ()

        // Serialize — lock on encoder because it is shared across all channels from the same
        // SslStreamChannelFactory (the factory holds one MessageEncoder instance and passes
        // it to every channel it creates).  Without this lock, concurrent RequestCore calls
        // from different channels race on the encoder's internal XmlDictionaryWriter pool,
        // producing "The Write method cannot be called when another write operation is pending."
        let bodyBytes =
            lock encoder (fun () ->
                let buf = encoder.WriteMessage(message, Int32.MaxValue, bufMgr, 0)
                let bytes = Array.init buf.Count (fun i -> buf.Array.[buf.Offset + i])
                bufMgr.ReturnBuffer(buf.Array)
                bytes)

        // Send via persistent SslStream; retry once on I/O failure (connection may have
        // gone stale between calls — one reconnect is enough).
        // Serialize the entire send/receive cycle on requestLock: SslStream permits only one
        // pending write and one pending read at a time, so concurrent channels sharing this
        // persistent connection must not overlap here or SslStream throws
        // "The Read/Write method cannot be called when another ... operation is pending."
        let mutable retried = false
        let mutable respOpt: SslStreamHelpers.HttpResponse option = None
        lock requestLock (fun () ->
            while respOpt.IsNone do
                let (_, ssl) = getOrCreateConnection timeoutMs
                try
                    SslStreamHelpers.sendOnSslStream ssl via bodyBytes contentType
                    respOpt <- Some (SslStreamHelpers.readHttpResponse ssl)
                with
                | ex when (ex :? IOException || ex :? SocketException) ->
                    clearConnection()
                    if retried then raise ex
                    retried <- true)

        let resp = respOpt.Value
        if resp.StatusCode >= 400 then
            System.Diagnostics.Debug.WriteLine(sprintf "SslStreamTransport: HTTP %d from %O" resp.StatusCode via)
            raise (CommunicationException(sprintf "HTTP %d received from camera at %O" resp.StatusCode via))

        // Deserialize response
        let respBuf = bufMgr.TakeBuffer(resp.Body.Length)
        Buffer.BlockCopy(resp.Body, 0, respBuf, 0, resp.Body.Length)
        let msg =
            lock encoder (fun () ->
                encoder.ReadMessage(ArraySegment<byte>(respBuf, 0, resp.Body.Length), bufMgr, resp.ContentType))
        // Mark all mustUnderstand response headers as understood before returning to WCF.
        // gSOAP cameras include Action mustUnderstand="1" in their response envelope;
        // WCF's ServiceChannel.HandleReply throws a FaultException for any mustUnderstand
        // header that has not been explicitly acknowledged by the channel.
        // Use Seq.iter (enumerator) rather than index loop: msg.Headers.[i] creates a new
        // wrapper object on each call, but UnderstoodHeaders.Add requires the exact same
        // object reference WCF tracks internally. The enumerator yields those tracked refs.
        msg.Headers
        |> Seq.filter (fun hdr -> hdr.MustUnderstand)
        |> Seq.iter (fun hdr ->
            try msg.Headers.UnderstoodHeaders.Add(hdr) with _ -> ())
        msg

    interface IRequestChannel with
        member _.RemoteAddress = address
        member _.Via = via
        member this.Request(message) =
            this.RequestCore(message, this.DefaultSendTimeout)
        member this.Request(message, timeout) =
            this.RequestCore(message, timeout)
        member this.BeginRequest(message, callback, state) =
            (this :> IRequestChannel).BeginRequest(message, this.DefaultSendTimeout, callback, state)
        member this.BeginRequest(message, timeout, callback, state) =
            let tcs = new TaskCompletionSource<Message>(state)
            Task.Factory.StartNew(fun () ->
                try tcs.SetResult(this.RequestCore(message, timeout))
                with ex -> tcs.SetException(ex)
            ) |> ignore
            if callback <> null then
                tcs.Task.ContinueWith(
                    Action<Task<Message>>(fun _ -> callback.Invoke(tcs.Task :> IAsyncResult))) |> ignore
            tcs.Task :> IAsyncResult
        member _.EndRequest(result) =
            let t = result :?> Task<Message>
            try t.Result
            with :? AggregateException as ae ->
                // ExceptionDispatchInfo is .NET 4.5+; on .NET 4.0 we re-raise the inner exception.
                // Stack trace is partially lost but this path is only hit via BeginRequest/EndRequest
                // which is not the primary call path for our integration tests.
                raise ae.InnerException
                Unchecked.defaultof<Message> // unreachable

    override _.GetProperty<'T when 'T : not struct>() =
        if typeof<'T> = typeof<ChannelParameterCollection> then
            channelParams :> obj :?> 'T
        else
            base.GetProperty<'T>()

    override _.OnAbort() =
        clearConnection()

    override _.OnOpen(_) = ()

    override _.OnClose(_) =
        clearConnection()

    override _.OnBeginOpen(_, callback, state) = completedAr callback state
    override _.OnEndOpen(_) = ()
    override _.OnBeginClose(_, callback, state) = completedAr callback state
    override _.OnEndClose(_) = ()


/// WCF ChannelFactory that creates SslStreamRequestChannel instances.
type SslStreamChannelFactory(timeouts: IDefaultCommunicationTimeouts,
                              encoderFactory: MessageEncoderFactory, wsAddressing: bool) =
    inherit ChannelFactoryBase<IRequestChannel>(timeouts)

    let encoder = encoderFactory.Encoder

    static let completedAr (callback: AsyncCallback) (state: obj) : IAsyncResult =
        let tcs = new TaskCompletionSource<bool>(state)
        tcs.SetResult(true)
        if callback <> null then callback.Invoke(tcs.Task :> IAsyncResult)
        tcs.Task :> IAsyncResult

    override this.OnCreateChannel(address: EndpointAddress, via: Uri) : IRequestChannel =
        new SslStreamRequestChannel(this, encoder, wsAddressing, address, via) :> IRequestChannel

    override _.GetProperty<'T when 'T : not struct>() : 'T =
        if typeof<'T> = typeof<MessageVersion> then
            encoderFactory.MessageVersion :> obj :?> 'T
        else
            base.GetProperty<'T>()

    override _.OnAbort() = ()
    override _.OnOpen(_) = ()
    override _.OnClose(_) = ()
    override _.OnBeginOpen(_, callback, state) = completedAr callback state
    override _.OnEndOpen(_) = ()
    override _.OnBeginClose(_, callback, state) = completedAr callback state
    override _.OnEndClose(_) = ()


/// WCF TransportBindingElement that uses raw TcpClient + SslStream
/// instead of HttpsTransportBindingElement to avoid multi-TLS-record issues.
type SslStreamTransportBindingElement() =
    inherit TransportBindingElement()

    override _.Scheme = Uri.UriSchemeHttps

    override _.Clone() =
        new SslStreamTransportBindingElement() :> BindingElement

    override _.CanBuildChannelFactory<'TChannel>(context: BindingContext) =
        typeof<'TChannel> = typeof<IRequestChannel>

    override _.BuildChannelFactory<'TChannel>(context: BindingContext) : IChannelFactory<'TChannel> =
        // Try BindingParameters first (populated by MessageEncodingBindingElement.BuildChannelFactory),
        // then fall back to GetInnerProperty (used by some WCF versions),
        // then default to plain SOAP 1.2 encoder.
        let encoderFactory =
            let fromParams = context.BindingParameters.Find<MessageEncoderFactory>()
            if fromParams <> null then fromParams
            else
                let fromProp = context.GetInnerProperty<MessageEncoderFactory>()
                if fromProp <> null then fromProp
                else
                    // No encoder in context — derive MessageVersion from the binding itself.
                    // context.Binding.MessageVersion reads the TextMessageEncodingBindingElement
                    // already in the binding, which is the same version WCF uses to create
                    // outgoing operation messages.  Using a hardcoded Soap12 here would cause
                    // a ProtocolException ("message version … does not match encoder") for
                    // WS-Addressing channels (Events/Metadata) whose binding declares
                    // Soap12WSAddressing10.
                    let msgVer =
                        let bv = context.Binding.MessageVersion
                        if bv = MessageVersion.None then MessageVersion.Soap12WSAddressing10
                        else bv
                    TextMessageEncodingBindingElement(msgVer, System.Text.Encoding.UTF8)
                        .CreateMessageEncoderFactory()
        let wsAddressing = encoderFactory.MessageVersion.Addressing <> AddressingVersion.None
        new SslStreamChannelFactory(context.Binding, encoderFactory, wsAddressing) :> obj :?> IChannelFactory<'TChannel>

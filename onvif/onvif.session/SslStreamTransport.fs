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

    let readAll (ssl: SslStream) =
        let buf = Array.zeroCreate 65536
        use ms = new MemoryStream()
        let mutable n = ssl.Read(buf, 0, buf.Length)
        while n > 0 do
            ms.Write(buf, 0, n)
            n <- ssl.Read(buf, 0, buf.Length)
        ms.ToArray()

    let getHeaderValue (headerBlock: string) (name: string) =
        let prefix = name + ":"
        headerBlock.Split([| "\r\n" |], StringSplitOptions.None)
        |> Array.tryFind (fun l -> l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        |> Option.map (fun l -> l.Substring(prefix.Length).Trim())

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

    let stripDoctype (data: byte[]) =
        // Remove <!DOCTYPE ...> declarations to prevent XmlReader from loading external DTDs.
        // Some cameras (e.g. Milesight Analytics) include DOCTYPE in SOAP response bodies,
        // causing XmlReader to look for XMLSchema.dtd on disk and fail with FileNotFoundException.
        let xml = Encoding.UTF8.GetString(data)
        let cleaned = Regex.Replace(xml, @"<!DOCTYPE[^>]*(?:>|(?:\[.*?\]>))", "", RegexOptions.Singleline)
        if cleaned.Length = xml.Length then data  // no DOCTYPE found, return original bytes
        else Encoding.UTF8.GetBytes(cleaned)

    type HttpResponse = {
        StatusCode: int
        ContentType: string
        Body: byte[]
    }

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

    let sslSend (uri: Uri) (bodyBytes: byte[]) (contentType: string) (timeoutMs: int) =
        let host = uri.Host
        let port = if uri.IsDefaultPort then 443 else uri.Port
        let hostHeader = if port = 443 then host else sprintf "%s:%d" host port
        let path = if String.IsNullOrEmpty(uri.PathAndQuery) then "/" else uri.PathAndQuery

        let headerStr =
            sprintf "POST %s HTTP/1.1\r\nHost: %s\r\nContent-Type: %s\r\nContent-Length: %d\r\nConnection: close\r\n\r\n"
                path hostHeader contentType bodyBytes.Length
        let headerBytes = Encoding.ASCII.GetBytes(headerStr)

        use tcp = new TcpClient()
        tcp.Connect(host, port)
        tcp.ReceiveTimeout <- timeoutMs
        tcp.SendTimeout <- timeoutMs

        use ssl = new SslStream(tcp.GetStream(), false,
                      RemoteCertificateValidationCallback(fun _ _ _ _ -> true))
        // SslProtocols.Tls12 = 0xC00 = 3072; enum value exists at runtime on .NET 4.0+
        // but the named constant was added to the BCL metadata only in .NET 4.5.
        // Cast the raw integer to avoid a compile-time reference to the 4.5-only symbol.
        ssl.AuthenticateAsClient(host, null, enum<SslProtocols> 3072, false)

        // Single ssl.Write() call: headers and body must arrive in one TLS record.
        // gSOAP cameras (2.8.x firmware) stall indefinitely when the TLS payload is
        // fragmented across two records, which is what .NET's HttpWebRequest does by default.
        let full = Array.zeroCreate (headerBytes.Length + bodyBytes.Length)
        Buffer.BlockCopy(headerBytes, 0, full, 0, headerBytes.Length)
        Buffer.BlockCopy(bodyBytes, 0, full, headerBytes.Length, bodyBytes.Length)
        ssl.Write(full)
        ssl.Flush()

        parseResponse (readAll ssl)


/// WCF IRequestChannel that sends SOAP via raw TcpClient + SslStream.
type SslStreamRequestChannel(factory: ChannelManagerBase, encoder: MessageEncoder,
                              wsAddressing: bool, address: EndpointAddress, via: Uri) =
    inherit ChannelBase(factory)

    let bufMgr = BufferManager.CreateBufferManager(int64 (64 * 1024 * 1024), Int32.MaxValue)
    // ChannelParameterCollection is required by WCF for security token propagation
    // (NvtSession.SetupUserNameToken adds SecurityUserNameToken here, then
    // CustomBehavior.BeforeSendRequest reads it back).
    let channelParams = new ChannelParameterCollection()

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

        // Serialize
        let buf = encoder.WriteMessage(message, Int32.MaxValue, bufMgr, 0)
        let rawBodyBytes = Array.init buf.Count (fun i -> buf.Array.[buf.Offset + i])
        bufMgr.ReturnBuffer(buf.Array)

        // Strip <Action s:mustUnderstand="1"> from the outgoing SOAP header, but only
        // for non-WS-Addressing channels. gSOAP camera firmware (2.8.x) returns HTTP 500
        // with a MustUnderstand SOAP fault for any WS-Addressing header it does not recognise.
        // However, WS-Addressing channels (Events/Metadata) use the Action header for
        // operation dispatch — stripping it causes cameras to return a dispatch error.
        let bodyBytes =
            if wsAddressing then
                rawBodyBytes  // WS-Addressing channels need Action header for operation dispatch
            else
                let xml = Encoding.UTF8.GetString(rawBodyBytes)
                // Match the Action open tag (which may span to >) then the content then the close tag.
                // The open tag ends with >, the content is the action URI, the close tag follows.
                // Pattern uses non-verbatim string: \" for quote in the pattern.
                let actionPattern = "<(?:[a-zA-Z0-9_]+:)?Action\\s[^>]*mustUnderstand=\"1\"[^>]*>.*?</(?:[a-zA-Z0-9_]+:)?Action>"
                let stripped = Regex.Replace(xml, actionPattern, "", RegexOptions.Singleline)
                // If the header block is now empty, remove it entirely
                let headerPattern = "<(?:[a-zA-Z0-9_]+:)?Header\\s*>\\s*</(?:[a-zA-Z0-9_]+:)?Header>"
                let stripped2 = Regex.Replace(stripped, headerPattern, "", RegexOptions.Singleline)
                Encoding.UTF8.GetBytes(stripped2)

        // Send via raw SslStream
        let resp = SslStreamHelpers.sslSend via bodyBytes contentType timeoutMs
        if resp.StatusCode >= 400 then
            System.Diagnostics.Debug.WriteLine(sprintf "SslStreamTransport: HTTP %d from %O" resp.StatusCode via)

            raise (CommunicationException(sprintf "HTTP %d received from camera at %O" resp.StatusCode via))


        // Deserialize response
        let respBuf = bufMgr.TakeBuffer(resp.Body.Length)
        Buffer.BlockCopy(resp.Body, 0, respBuf, 0, resp.Body.Length)
        let msg =
            encoder.ReadMessage(ArraySegment<byte>(respBuf, 0, resp.Body.Length), bufMgr, resp.ContentType)
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

    override _.OnAbort() = ()
    override _.OnOpen(_) = ()
    override _.OnClose(_) = ()
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

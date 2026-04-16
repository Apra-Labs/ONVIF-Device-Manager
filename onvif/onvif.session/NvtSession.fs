namespace odm.core

    open System
    open System.Collections.Generic
    open System.Diagnostics
    open System.Globalization
    open System.IdentityModel.Tokens
    open System.IdentityModel.Selectors
    open System.Linq
    open System.Net
    open System.Net.Sockets
    open System.Security.Cryptography
    open System.ServiceModel
    open System.ServiceModel.Channels
    open System.ServiceModel.Description
    open System.ServiceModel.Dispatcher
    open System.ServiceModel.Security
    open System.ServiceModel.Security.Tokens
    open System.Text
    open System.Threading
    open System.Xml
    open System.Xml.Linq
    open System.Xml.Schema

    open System.Reactive.Disposables

    open odm.onvif
    open utils
    open utils.fsharp

    open onvif
    open onvif.services
    open onvif10_device
    open onvif10_network
    open onvif20_ptz
    open onvif10_media
    open onvif10_events
    open onvif10_analytics
    open onvif10_deviceio
    open onvif10_imaging
    open onvif10_receiver
    open onvif10_display
    open onvif10_analyticsdevice
    open onvif10_recording
    open onvif10_replay
    open onvif10_search

    type NetworkInterface = onvif.services.NetworkInterface
    type IPAddress = onvif.services.IPAddress
    
    [<AllowNullLiteral>]
    [<ServiceContract>]
    type IOnvifServices = interface
        inherit Device
        inherit Media
        inherit PTZ
        inherit IEvent
        inherit IAnalytics
        inherit ImagingPort
        //inherit Network
        inherit ReceiverPort
        inherit DeviceIOPort
        inherit DisplayPort
        inherit AnalyticsDevicePort
        inherit RecordingPort
        inherit ReplayPort
        inherit SearchPort
        inherit ActionEnginePort
    end

    [<AllowNullLiteral>]
    type INvtSession = interface
        inherit IDeviceAsync
        inherit IMediaAsync
        inherit IPtzAsync
        inherit IEventAsync
        inherit IAnalyticsAsync
        inherit IImagingAsync
        inherit IReceiverAsync
        inherit IAnalyticsDeviceAsync
        inherit IActionEngineAsync
        inherit IReplayAsync
        abstract CreatePullPointSubscription: filter:FilterType * initialTerminationTime:string * subscriptionPolicy:CreatePullPointSubscriptionSubscriptionPolicy->Async<ISubscriptionManagerAsync>
        abstract CreateBaseSubscription: consumerReference:EndpointReferenceType1* filter:FilterType* initialTerminationTime:string* subscriptionPolicy:SubscribeSubscriptionPolicy->Async<ISubscriptionManagerAsync>
        abstract credentials:NetworkCredential
        abstract deviceUri:Uri
        abstract GetAllCapabilities: unit -> Async<Capabilities>
    end

    type private ServiceEndpointMap = {
        Media1XAddr: string option
        Media2XAddr: string option
        PtzXAddr: string option
        ImagingXAddr: string option
        EventsXAddr: string option
        AnalyticsXAddr: string option
        ReceiverXAddr: string option
        AnalyticsDeviceXAddr: string option
        RecordingXAddr: string option
        ReplayXAddr: string option
        ActionEngineXAddr: string option
    }
    with
        member this.HasMedia2 = this.Media2XAddr.IsSome
    
    type SecurityUserNameToken(userName:string, password:string, deviceTime:System.DateTime) = class
        let delta = 
            let systemTicks = int64(float(Stopwatch.GetTimestamp()) * (10000000.0/ float(Stopwatch.Frequency)))
            deviceTime.Ticks - systemTicks
        member this.userName:string = userName
        member this.password:string = password
        //member this.delta:int64 = delta
        member this.GetCurrentDeviceTime() = 
            let ticksEllapsed = int64(float(Stopwatch.GetTimestamp()) * (10000000.0/ float(Stopwatch.Frequency)))
            //let created = (new System.DateTime(ticks + secToken.delta)).ToString(@"yyyy\-MM\-dd\THH\:mm\:ss\.fff\Z")
            (new System.DateTime(ticksEllapsed + delta))
            
        //member this.systemTimeStamp:int64 = systemTimeStamp
        //member this.deviceDateTime:int64 = deviceDateTime
    end

    type CustomBehavior() = class
        let wsse = @"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"
        let wsu = @"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"
        
        static let getNonceCounter = 
            let cnt = ref 0L
            fun()->Interlocked.Increment(cnt)

        static let guid = Guid.NewGuid().ToByteArray()

        static let ComputePasswordDigest(nonce:seq<byte>, created:string, password:string) =
            let data = 
                seq{
                    yield! nonce
                    yield! created |> Encoding.UTF8.GetBytes
                    yield! password |> Encoding.UTF8.GetBytes
                } |> Seq.toArray
            let hasher = SHA1.Create()
            let hash = hasher.ComputeHash(data)
            Convert.ToBase64String(hash)

        //let soap = @"http://www.w3.org/2003/05/soap-envelope"
        let CreateUserNameToken(userName:string, password:string, created:string) =
            let passwordType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest"
            let nonceEncodingType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"
            //let created = DateTime.Now.ToUniversalTime().ToString(@"yyyy\-MM\-dd\THH\:mm\:ss\.fff\Z")
            let nonce = Seq.toArray(seq{
                yield! guid
                yield! (getNonceCounter() |> BitConverter.GetBytes ).[0..5]
                //yield! Stopwatch.GetTimestamp() |> BitConverter.GetBytes
//                  yield! Convert.FromBase64String("wRR0n97vaDmhFHxA5Nzyxg==")
            })
            let nonce_base64 = Convert.ToBase64String(nonce)
            let pswd_digest = ComputePasswordDigest(nonce, created, password)
            new XElement(XName.Get("UsernameToken", wsse),
                new XElement(XName.Get("Username", wsse), userName),
                new XElement(XName.Get("Password", wsse),
                    new XAttribute(XName.Get("Type"), passwordType),
                    new XText(pswd_digest)
                ),
                new XElement(XName.Get("Nonce", wsse),
                    new XAttribute(XName.Get("EncodingType"), nonceEncodingType),
                    new XText(nonce_base64)
                ),
                new XElement(XName.Get("Created", wsu), created)
            )
        
        
        let CreateSecurityHeader(channel:IClientChannel) =
            let paramCollection = channel.GetProperty<ChannelParameterCollection>()
            let secToken = paramCollection.OfType<SecurityUserNameToken>().First()
            let userName = secToken.userName
            let password = secToken.password
            //let ticks = DateTime.Now.ToUniversalTime().Ticks - secToken.systemTimeStamp
            //let created = DateTime.UtcNow.AddTicks(secToken.delta).ToString(@"yyyy\-MM\-dd\THH\:mm\:ss\.fff\Z")
            //let ticks = int64(float(Stopwatch.GetTimestamp()) * (10000000.0/ float(Stopwatch.Frequency)))
            //let created = (new System.DateTime(ticks + secToken.delta)).ToString(@"yyyy\-MM\-dd\THH\:mm\:ss\.fff\Z")
            let created = secToken.GetCurrentDeviceTime().ToString(@"yyyy\-MM\-dd\THH\:mm\:ss\.fff\Z")
//            let nonce = Seq.toArray(seq{
//                yield! guid
//                yield! getNonceCounter() |> BitConverter.GetBytes
//                yield! Stopwatch.GetTimestamp() |> BitConverter.GetBytes
//            })
//            let nonce_base64 = Convert.ToBase64String(nonce)
//            let pswd_digest = ComputePasswordDigest(nonce, created, password)

            let header = {
                new MessageHeader() with
                    override this.Name = "Security"
                    override this.Namespace = wsse
                    override this.MustUnderstand = true
                    override this.OnWriteHeaderContents(writer:XmlDictionaryWriter, messageVersion:MessageVersion) = 
                        let xcontent = CreateUserNameToken(userName, password, created)
                        xcontent.WriteTo(writer)
                end
            }
            header
        static member ComputePasswordDigest1(nonce:seq<byte>, created:string, password:string) =
            ComputePasswordDigest(nonce, created, password)

        interface IEndpointBehavior with
            override this.AddBindingParameters(endpoint:ServiceEndpoint, bindingParameters:BindingParameterCollection) =
                ()
            override this.ApplyClientBehavior(endpoint:ServiceEndpoint, clientRuntime:ClientRuntime) =
                for op in endpoint.Contract.Operations do
                    ()
                let interceptor = {
                    new IClientMessageInspector with
                        override this.AfterReceiveReply(reply:byref<System.ServiceModel.Channels.Message>, correlationState:obj) = 
                            match reply.Headers |> Seq.tryFind(fun hdr->hdr.Name = "Security" && hdr.Namespace = wsse) with
                            |Some secHdr ->
                                // Guard against duplicate: SslStreamTransport pre-marks all mustUnderstand
                                // headers (including Security) as understood before the message inspector
                                // runs, so a second Add throws ArgumentException on HTTPS sessions.
                                try reply.Headers.UnderstoodHeaders.Add(secHdr) with _ -> ()
                            |None -> ()
                        override this.BeforeSendRequest(request:byref<System.ServiceModel.Channels.Message>, channel:IClientChannel) = 
                            let header = CreateSecurityHeader(channel)
                            request.Headers.Add(header)
                            null
                    end
                }
//                for op in clientRuntime.o .Operations do
//                    op.Be
//                    let innerBeginF = op.BeginMethod
//                    let innerEndF = op.EndMethod
//                    let innerAsync = Async.FromBeginEnd(innerBeginF, innerEndF)
//                    let wrappedAsync = async{
//                        return! innerAsync
//                    }
//                    op.
                clientRuntime.MessageInspectors.Add(interceptor)
//                let errHandler = {
//                    new IErrorHandler with
//                        override this.HandleError(error:Exception) = 
//                            false
//                        override this.ProvideFault(error:Exception, messageVersion:MessageVersion, message:byref<Message>) = 
//                            ()
//                    end
//                }
//                clientRuntime.CallbackDispatchRuntime.E
            override this.ApplyDispatchBehavior(endpoint:ServiceEndpoint, endpointDispatcher:EndpointDispatcher) =
                ()
            override this.Validate(endpoint:ServiceEndpoint) =
                ()
        end
    end

    type NvtSessionFactory(credentials: NetworkCredential) = class

        static let AlternateImplementation (comp:Async<'T>) (altComp:Async<'T>):Async<'T> = 
            let tramp = new Trampoline()
            let useAlt = false
            let impl = async{
                try
                    return! comp
                with 
                    | :? FaultException as fault when (fault.Code.SubCode.Name) = "ActionNotSupported" && (fault.Code.SubCode.Namespace) = "http://www.onvif.org/ver10/error" ->
                        return! altComp
                    | err -> 
                        dbg.Error(err)
                        return raise err
            }
            impl

//            let tramp = new Trampoline()
//            let state = ref MemoizedAsyncState<'T>.Idle
//            let subscribe (observers:LinkedList<AsyncObserver<'T>>) (observer:AsyncObserver<'T>) =
//                let node = observers.AddLast(observer)
//                Disposable.Create(fun()->
//                    tramp.Drop(fun()->
//                        observers.Remove(node)
//                    )
//                )
//            let StartComp() =
//                let observers = new LinkedList<AsyncObserver<'T>>()
//                let CompCompleted(result: 'T) = tramp.Drop(fun()->
//                    state := Completed(result)
//                    for (onSuccess, onError) in observers do
//                        onSuccess(result)
//                )
//                let CompFailed(error: Exception) = tramp.Drop(fun()->
//                    state := Failed(error)
//                    for (onSuccess, onError) in observers do
//                        onError(error)
//                )
//                Async.StartWithContinuations(
//                    comp,
//                    CompCompleted,
//                    CompFailed,
//                    CompFailed
//                )
//                state := Processing(observers)
//                observers
//
//            Async.CreateWithDisposable(fun (onSuccess, onError)->
//                let disp = new SingleAssignmentDisposable()
//                tramp.Drop(fun()->
//                    match !state with
//                    |Idle ->
//                        let observers = StartComp()
//                        disp.Disposable <- (onSuccess, onError) |> subscribe observers
//                    |Processing observers ->
//                        disp.Disposable <- (onSuccess, onError) |> subscribe observers
//                    |Completed result ->
//                        onSuccess result
//                    |Failed err ->
//                        onError err
//                )
//                disp :> IDisposable
//            )

        static let memoize f =
            let result = ref None
            fun()->
                match !result with
                | Some value -> value
                | None ->
                    let x = f()
                    result := Some x
                    x

        let factory_wrapper f = 
            let httpFactory = memoize (fun()->f(false))
            let httpsFactory = memoize (fun()->f(true))
            fun useTls ->
                if useTls then
                    lock httpsFactory httpsFactory
                else
                    lock httpFactory httpFactory

        let getDeviceUnsecureFactory = 
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<Device>(false, false, false, useTls)
            }))
            fun(useTls)->comp(useTls)

        let getDeviceFactory = 
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<Device>(false, false, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)
        
        let getDeviceMtomFactory = 
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<Device>(true, false, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)
        
        let getMediaFactory = 
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<Media>(false, false, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)
        
        let getPtzFactory = 
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<PTZ>(false, false, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)
        
        let getImagingFactory = 
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<ImagingPort>(false, false, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)
        
        let getAnalyticsFactory = 
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<IAnalytics>(false, false, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)
        
        let getReceiverFactory = 
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<ReceiverPort>(false, false, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)

        let getRecordingFactory = 
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<RecordingPort>(false, false, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)

        let getAnalyticsDeviceFactory = 
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<AnalyticsDevicePort>(false, false, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)
//        let getRuleEngineFactory = 
//            let comp = Async.Memoize(async{
//                do! Async.SwitchToThreadPool()
//                return new ChannelFactory<RuleEnginePort>(CreateBinding(false, false)) |> ConfigChannelFactory
//            })
//            fun()->comp
        let getEventFactory = 
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<IEvent>(false, true, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)
            
        let getActionEngineFactory = 
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<ActionEnginePort>(false, false, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)

        let getReplayFactory = 
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<ReplayPort>(false, false, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)

        let getSubManFactory =
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<ISubscriptionManager>(false, true, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)

        let getMedia2Factory =
            let comp = factory_wrapper (fun(useTls)-> Async.Memoize(async{
                do! Async.SwitchToThreadPool()
                return NvtSessionFactory.CreateChannelFactory<IMedia2>(false, false, credentials |> NotNull, useTls)
            }))
            fun(useTls)->comp(useTls)


        static member private CreateChannelFactory<'T>(mtomEncoding:bool, wsAddressing:bool, securityToken: bool, useTls: bool):ChannelFactory<'T> =
            let binding = 
                let bindingElements = seq{
                    let msgVer =
                        if wsAddressing then
                            MessageVersion.Soap12WSAddressing10
                        else
                            MessageVersion.Soap12
                    
                    if mtomEncoding then 
                        let encoding = new MtomMessageEncodingBindingElement(msgVer, Encoding.UTF8)
                        encoding.ReaderQuotas.MaxStringContentLength <- Int32.MaxValue
                        encoding.MaxBufferSize <- Int32.MaxValue
                        yield encoding :> BindingElement
                    else
                        let encoding = new TextMessageEncodingBindingElement(msgVer, Encoding.UTF8)
                        encoding.ReaderQuotas.MaxStringContentLength <- Int32.MaxValue //100 * 1024 * 1024
                        yield encoding :> BindingElement

                    if useTls then
                        // SslStreamTransportBindingElement replaces HttpsTransportBindingElement.
                        // It sends the request as a single TLS record, fixing gSOAP multi-record bug.
                        yield new SslStreamTransportBindingElement() :> BindingElement
                    else
                        let transport = new HttpTransportBindingElement()
                        transport.MaxReceivedMessageSize <- int64(Int32.MaxValue) //100L * 1024L * 1024L
                        transport.KeepAliveEnabled <- false
                        transport.MaxBufferSize <- Int32.MaxValue
                        transport.ProxyAddress <- null
                        transport.BypassProxyOnLocal <- true
                        //transport.ManualAddressing <- true
                        transport.UseDefaultWebProxy <- false
                        transport.TransferMode <- TransferMode.StreamedResponse
                        yield transport :> BindingElement
                }
                new CustomBinding(bindingElements)
            binding.CloseTimeout <- TimeSpan.FromSeconds(30.0)
            binding.OpenTimeout <- TimeSpan.FromSeconds(30.0)
            //binding.SendTimeout <- TimeSpan.FromMinutes(3.0)
            binding.SendTimeout <- TimeSpan.FromMinutes(10.0)
            binding.ReceiveTimeout <- TimeSpan.FromMinutes(3.0)
            
            let factory = new ChannelFactory<'T>(binding)
            if securityToken then 
                factory.Endpoint.Behaviors.Add(new CustomBehavior())
            factory

        /// Upgrades an HTTP URL to HTTPS when the session's device was reached via HTTPS.
        /// Maps port 80 -> 443; keeps other non-standard ports unchanged.
        /// Publicly accessible for unit testing (mirrors the private UpgradeSchemeIfNeeded closure).
        static member UpgradeScheme (deviceUri: Uri) (url: Uri) : Uri =
            if deviceUri.Scheme = Uri.UriSchemeHttps && url.Scheme = Uri.UriSchemeHttp then
                let b = new UriBuilder(url)
                b.Scheme <- Uri.UriSchemeHttps
                let httpsPort = if deviceUri.IsDefaultPort then 443 else deviceUri.Port
                b.Port <- httpsPort
                b.Uri
            else
                url

        /// Generates HTTPS URI variants from HTTP URIs for scheme-upgrade fallback.
        /// Called when all HTTP SOAP probes fail: some cameras advertise only http:// xAddrs
        /// via WS-Discovery yet actually require HTTPS. Produces port-443 and port-8443 variants
        /// for each HTTP URI; already-HTTPS inputs return empty (no upgrade needed).
        /// Publicly accessible for unit testing.
        static member GenerateHttpsVariants (uris: Uri[]) : Uri[] =
            uris
            |> Array.collect (fun uri ->
                if uri.Scheme = Uri.UriSchemeHttp then
                    let port = if uri.IsDefaultPort || uri.Port = 80 then 443 else uri.Port
                    let builder443 = new UriBuilder(uri)
                    builder443.Scheme <- Uri.UriSchemeHttps
                    builder443.Port <- port
                    let builder8443 = new UriBuilder(uri)
                    builder8443.Scheme <- Uri.UriSchemeHttps
                    builder8443.Port <- 8443
                    if port = 8443 then
                        [| builder443.Uri |]
                    else
                        [| builder443.Uri; builder8443.Uri |]
                elif uri.Scheme = Uri.UriSchemeHttps then
                    [||]  // Already HTTPS — no upgrade variants needed
                else
                    [||]
            )
            |> Seq.distinct |> Seq.toArray

        member this.CreateSession(uris:Uri[]) = async{
            /// SOAP-level probe: sends GetSystemDateAndTime (unauthenticated) to verify
            /// the endpoint actually responds to ONVIF requests. TCP connectivity alone
            /// is insufficient — some cameras bind port 80 but silently drop SOAP payloads.
            let SoapProbe (uri:Uri) = async{
                try
                    do! Async.SwitchToThreadPool()
                    let useTls = uri.Scheme = Uri.UriSchemeHttps
                    let! factory = getDeviceUnsecureFactory(useTls)
                    let endpointAddr = new EndpointAddress(uri)
                    let proxy = factory.CreateChannel(endpointAddr)
                    let dev = new DeviceAsync(proxy) :> IDeviceAsync
                    let! _dateTime = dev.GetSystemDateAndTime()
                    try (proxy :?> ICommunicationObject).Abort() with _ -> ()
                    return true
                with _ ->
                    return false
            }

            let soapProbeWithTimeout (uri:Uri) (timeoutMs:int) = async{
                let! child = Async.StartChild(SoapProbe(uri), timeoutMs)
                try
                    let! result = child
                    return result
                with
                | :? TimeoutException ->
                    return false
            }

            // Sequential probe: tries each URI one at a time rather than in parallel.
            // Some cameras (e.g. Hikvision) drop or reject connections when multiple
            // simultaneous TCP connections arrive during negotiation.
            let raceEndpoints (srcUris:Uri[]) = async {
                let distinctUris = srcUris |> Seq.distinct |> Seq.toList
                let probeOne (uri:Uri) = async {
                    let! cr = soapProbeWithTimeout uri 5000
                    if cr then
                        log.WriteInfo(sprintf "SOAP probe passed on %s" (uri.ToString()))
                        return Some uri
                    else
                        log.WriteInfo(sprintf "SOAP probe failed on %s" (uri.ToString()))
                        return None
                }
                let rec tryFirst (uris:Uri list) = async {
                    match uris with
                    | [] -> return None
                    | uri :: rest ->
                        let! result = probeOne uri
                        match result with
                        | Some _ -> return result
                        | None -> return! tryFirst rest
                }
                return! tryFirst distinctUris
            }

            if uris.Length = 0 then return failwith("no uri was passed")

            // SecurityProtocolType.Tls12 (3072) and Tls13 (12288) are not named constants
            // in .NET 4.0/4.5 reference assemblies; use integer casts for cross-target compatibility.
            ServicePointManager.SecurityProtocol <- enum<SecurityProtocolType>(3072 ||| 12288)
            ServicePointManager.Expect100Continue <- false
            ServicePointManager.ServerCertificateValidationCallback <- fun _ _ _ _ -> true

            // Try original URIs first (SOAP-level probe, not just TCP)
            let! httpResult = raceEndpoints uris
            match httpResult with
            | Some uri ->
                return this.CreateSession(uri)
            | None ->
                // SOAP probe failed on all original URIs — try HTTPS variants
                let httpsUris = NvtSessionFactory.GenerateHttpsVariants uris
                if httpsUris.Length = 0 then
                    return failwith("SOAP probe failed for all URIs")
                log.WriteInfo(sprintf "HTTP SOAP probe failed for %d URIs, retrying with HTTPS variants" uris.Length)
                let! httpsResult = raceEndpoints httpsUris
                match httpsResult with
                | Some uri ->
                    return this.CreateSession(uri)
                | None ->
                    return failwith("SOAP probe failed for all URIs (including HTTPS fallback)")

//            let cts = new CancellationTokenSource()
//            use! cancellation = Async.OnCancel(fun ()-> 
//                cts.Cancel()
//            )
//            let TryCreateSession(uris:seq<Uri>) = Async.FromContinuations(fun (success, error, cancel) ->
//                let attempts = ref 1
//                let completed = ref false
//                let tramp = new Trampoline()
//                for uri in uris do 
//                    tramp.Drop(fun()->
//                        attempts:= !attempts+1
//                        //Interlocked.Increment(attempts)
//                        let session = this.CreateSession(uri)
//                        let dev = session :> IDeviceAsync
//                        Async.StartWithContinuations(
//                            dev.GetCapabilities(),
//                            (fun v-> tramp.Drop(fun ()->
//                                if not !completed then
//                                    completed:=true
//                                    try success session with err->Assert.FailWithError err
//                                    cts.Cancel()
//                            )),
//                            (fun e->tramp.Drop(fun()->
//                                attempts:= !attempts - 1 
//                                if !attempts = 0 && not !completed then
//                                    completed:=true
//                                    try error e with err->Assert.FailWithError err
//                            )),
//                            (fun c->tramp.Drop(fun()->
//                                attempts:= !attempts - 1
//                                if !attempts = 0 && not !completed then
//                                    completed:=true
//                                    try cancel c with err->Assert.FailWithError err
//                            )),
//                            cts.Token
//                        )
//                    )
//                tramp.Drop(fun()->
//                    attempts:= !attempts - 1
//                    if !attempts = 0 && not !completed then
//                        completed:=true
//                        try 
//                            error <| new Exception("failed to create session") 
//                        with 
//                            err->Assert.FailWithError err
//                )
//            )
//            let IsLinkLocalAddress(uri:Uri) = 
//                let ipAddr = 
//                    match uri.HostNameType with
//                    |UriHostNameType.IPv4 -> IPAddress.Parse(uri.Host)
//                    |UriHostNameType.IPv6 -> IPAddress.Parse(uri.Host)
//                    |_->null
//                if ipAddr |> IsNull then
//                    false
//                else
//                    let addrBytes = ipAddr.GetAddressBytes()
//                    ipAddr.IsIPv6LinkLocal || 
//                    (ipAddr.AddressFamily = AddressFamily.InterNetwork && addrBytes.[0] = 169uy && addrBytes.[1] = 254uy)
//            return! TryCreateSession(uris)
            
            //try
                //let uris1 = uris.Where(fun x -> not(IsLinkLocalAddress(x)))
                //return! TryCreateSession(uris1)
            //with err-> 
                //let uris2 = uris.Where(fun x -> IsLinkLocalAddress(x))
                //return! TryCreateSession(uris2)
        }

        member this.CreateSession(deviceUri:Uri) =
            log.WriteInfo(sprintf "creating session for %s" (deviceUri.ToString()))
            ServicePointManager.FindServicePoint(deviceUri).Expect100Continue <- false
            let sessionId = obj()
            let GetDeviceUnsecureClient = 
                let comp = Async.Memoize(async{
                    do! Async.SwitchToThreadPool()
                    let endpointAddr = new EndpointAddress(deviceUri)
                    let useTls = deviceUri.Scheme = Uri.UriSchemeHttps
                    let! factory = getDeviceUnsecureFactory(useTls)
                    let proxy = factory.CreateChannel(endpointAddr)
                    return (new DeviceAsync(proxy) :> IDeviceAsync)
                })
                fun()->comp

            let GetSecurityUserNameToken = 
                let comp = Async.Memoize(async{
                    let! dev = GetDeviceUnsecureClient()
                    let! deviceTime = async{
                        try
                            let! dateTime = dev.GetSystemDateAndTime()
                            return new System.DateTime(
                                dateTime.utcDateTime.date.year,
                                dateTime.utcDateTime.date.month,
                                dateTime.utcDateTime.date.day,
                                dateTime.utcDateTime.time.hour,
                                dateTime.utcDateTime.time.minute,
                                dateTime.utcDateTime.time.second,
                                DateTimeKind.Utc
                            )
                        with err->
                            dbg.Error(err)
                            return DateTime.UtcNow
                            //return DateTime.Now.ToUniversalTime()
                        
                    }
                    return new SecurityUserNameToken(
                        credentials.UserName, 
                        credentials.Password, 
                        deviceTime
                    )
                })
                fun()->comp

            let SetupUserNameToken(channel:IClientChannel) = async{
                if credentials |> NotNull then
                    let! secToken = GetSecurityUserNameToken()
                    let paramCol = channel.GetProperty<ChannelParameterCollection>()
                    paramCol.Add(secToken)
            }

            let GetDeviceClient = 
                let comp = Async.Memoize(async{
                    dbg.Info(sprintf "%08X::%s" (sessionId.GetHashCode()) "GetDeviceClient") 
                    do! Async.SwitchToThreadPool()
                    let endpointAddr = new EndpointAddress(deviceUri)
                    let useTls = deviceUri.Scheme = Uri.UriSchemeHttps
                    let! factory = getDeviceFactory(useTls)
                    let proxy = factory.CreateChannel(endpointAddr)
                    do! SetupUserNameToken(proxy :?> IClientChannel)
                    return (new DeviceAsync(proxy) :> IDeviceAsync)
                })
                if credentials |> NotNull then
                    fun()->comp
                else
                    GetDeviceUnsecureClient

            let GetDeviceMtomClient = 
                let comp = Async.Memoize(async{
                    dbg.Info(sprintf "%08X::%s" (sessionId.GetHashCode()) "GetDeviceMtomClient") 
                    do! Async.SwitchToThreadPool()
                    let useTls = deviceUri.Scheme = Uri.UriSchemeHttps
                    let! factory = getDeviceMtomFactory(useTls)
                    let endpointAddr = new EndpointAddress(deviceUri)
                    let proxy = factory.CreateChannel(endpointAddr)
                    do! SetupUserNameToken(proxy :?> IClientChannel)
                    return (new DeviceAsync(proxy) :> IDeviceAsync)
                })
                fun()->comp

            let GetCapabilities = 
                let comp = Async.Memoize(async{
                    let! dev = GetDeviceClient()
                    return! dev.GetCapabilities()
                })
                fun()->comp

            let GetServiceCapabilities = 
                let comp = Async.Memoize(async{
                    let! dev = GetDeviceClient()
                    return! dev.GetServiceCapabilities()
                })
                fun()->comp

            let GetServices =
                let comp = Async.Memoize(async{
                    let! dev = GetDeviceClient()
                    return! dev.GetServices(false)
                })
                fun()->comp

            // Resolves all service xAddrs at session creation time using a three-level fallback:
            // 1. GetServices() namespace match, 2. GetCapabilities() field, 3. Constructed default URL.
            // Started eagerly in the background so service clients find results already cached.
            let GetResolvedEndpoints =
                let comp = Async.Memoize(async{
                    let! servicesOpt = async{
                        try
                            let! svcs = GetServices()
                            return if svcs |> IsNull then None else Some svcs
                        with _ -> return None
                    }
                    let! capsOpt = async{
                        try
                            let! caps = GetCapabilities()
                            return if caps |> IsNull then None else Some caps
                        with _ -> return None
                    }
                    let findServiceXAddr ns =
                        servicesOpt |> Option.bind (fun svcs ->
                            svcs |> Seq.tryFind (fun (s:Service) -> s.Namespace = ns)
                            |> Option.bind (fun s ->
                                if String.IsNullOrEmpty(s.XAddr) then None else Some s.XAddr
                            )
                        )
                    let getCapsXAddr (f: Capabilities -> string) =
                        capsOpt |> Option.bind (fun caps ->
                            try
                                let xAddr = f caps
                                if String.IsNullOrEmpty(xAddr) then None else Some xAddr
                            with _ -> None
                        )
                    let constructUrl path =
                        let b = new UriBuilder(deviceUri)
                        b.Path <- path
                        b.Query <- ""
                        Some (b.Uri.ToString())
                    let resolve ns capsGetter defaultPath =
                        match findServiceXAddr ns with
                        | Some x -> Some x
                        | None ->
                            match getCapsXAddr capsGetter with
                            | Some x -> Some x
                            | None -> constructUrl defaultPath
                    let resolveNoLastResort ns capsGetter =
                        match findServiceXAddr ns with
                        | Some x -> Some x
                        | None -> getCapsXAddr capsGetter
                    return {
                        Media1XAddr = resolve "http://www.onvif.org/ver10/media/wsdl"
                                        (fun c -> c.media.xAddr)
                                        "/onvif/media_service"
                        Media2XAddr = findServiceXAddr "http://www.onvif.org/ver20/media/wsdl"
                        PtzXAddr = resolve "http://www.onvif.org/ver20/ptz/wsdl"
                                     (fun c -> c.ptz.xAddr)
                                     "/onvif/ptz_service"
                        ImagingXAddr = resolve "http://www.onvif.org/ver10/imaging/wsdl"
                                         (fun c -> c.imaging.xAddr)
                                         "/onvif/imaging_service"
                        EventsXAddr = resolve "http://www.onvif.org/ver10/events/wsdl"
                                        (fun c -> c.events.xAddr)
                                        "/onvif/events_service"
                        AnalyticsXAddr = resolveNoLastResort "http://www.onvif.org/ver20/analytics/wsdl"
                                           (fun c -> c.analytics.xAddr)
                        ReceiverXAddr = resolveNoLastResort "http://www.onvif.org/ver10/receiver/wsdl"
                                          (fun c -> c.extension.receiver.xAddr)
                        AnalyticsDeviceXAddr = resolveNoLastResort "http://www.onvif.org/ver20/analyticsdevice/wsdl"
                                                 (fun c -> c.extension.analyticsDevice.xAddr)
                        RecordingXAddr = resolveNoLastResort "http://www.onvif.org/ver10/recording/wsdl"
                                           (fun c -> c.extension.recording.xAddr)
                        ReplayXAddr = resolveNoLastResort "http://www.onvif.org/ver10/replay/wsdl"
                                        (fun c -> c.extension.replay.xAddr)
                        ActionEngineXAddr = findServiceXAddr "http://www.onvif.org/ver10/actionengine/wsdl"
                    }
                })
                fun() -> comp


            let GetDeviceInformation = 
                let comp = Async.Memoize(async{
                    let! dev = GetDeviceClient()
                    return! dev.GetDeviceInformation()
                })
                fun() -> comp

            // Upgrade an HTTP URL to HTTPS when the device was reached via HTTPS.
            // Delegates to the public static UpgradeScheme — single source of truth.
            let UpgradeSchemeIfNeeded (url: Uri) = NvtSessionFactory.UpgradeScheme deviceUri url

            // Returns true for addresses that are loopback/unroutable and must be replaced with
            // the device's reachable host. Never touch the port — only substitute the host.
            let isUnroutableHost (host: string) =
                String.IsNullOrEmpty(host) ||
                host = "127.0.0.1" ||
                host = "0.0.0.0" ||
                host = "::1" ||
                String.Compare(host, "localhost", StringComparison.OrdinalIgnoreCase) = 0

            let FixUrl(url:Uri) = async{
                let resolved =
                    if not(url.IsAbsoluteUri) then
                        new Uri(deviceUri, url)
                    elif isUnroutableHost url.Host then
                        url.Relocate(deviceUri.Host)
                    else
                        url
                return UpgradeSchemeIfNeeded resolved
            }

            let GetMediaClient =
                let comp = Async.Memoize(async{
                    dbg.Info(sprintf "%08X::%s" (sessionId.GetHashCode()) "GetMediaClient")
                    let! eps = GetResolvedEndpoints()
                    match eps.Media1XAddr with
                    | None -> return null
                    | Some xaddr ->
                        do! Async.SwitchToThreadPool()
                        let! url = FixUrl(new Uri(xaddr, UriKind.RelativeOrAbsolute))
                        let useTls = url.Scheme = Uri.UriSchemeHttps
                        let! factory = getMediaFactory(useTls)
                        let endpointAddr = new EndpointAddress(url)
                        let proxy = factory.CreateChannel(endpointAddr)
                        do! SetupUserNameToken(proxy :?> IClientChannel)
                        return (new MediaAsync(proxy) :> IMediaAsync)
                })
                fun()->comp

            let GetImagingClient =
                let comp = Async.Memoize(async{
                    dbg.Info(sprintf "%08X::%s" (sessionId.GetHashCode()) "GetImagingClient")
                    let! eps = GetResolvedEndpoints()
                    match eps.ImagingXAddr with
                    | None -> return null
                    | Some xaddr ->
                        do! Async.SwitchToThreadPool()
                        let! url = FixUrl(new Uri(xaddr, UriKind.RelativeOrAbsolute))
                        let useTls = url.Scheme = Uri.UriSchemeHttps
                        let! factory = getImagingFactory(useTls)
                        let endpointAddr = new EndpointAddress(url)
                        let proxy = factory.CreateChannel(endpointAddr)
                        do! SetupUserNameToken(proxy :?> IClientChannel)
                        return (new ImagingAsync(proxy) :> IImagingAsync)
                })
                fun()->comp

            let GetPtzClient =
                let comp = Async.Memoize(async{
                    dbg.Info(sprintf "%08X::%s" (sessionId.GetHashCode()) "GetPtzClient")
                    let! eps = GetResolvedEndpoints()
                    match eps.PtzXAddr with
                    | None -> return null
                    | Some xaddr ->
                        do! Async.SwitchToThreadPool()
                        let! url = FixUrl(new Uri(xaddr, UriKind.RelativeOrAbsolute))
                        let useTls = url.Scheme = Uri.UriSchemeHttps
                        let! factory = getPtzFactory(useTls)
                        let endpointAddr = new EndpointAddress(url)
                        let proxy = factory.CreateChannel(endpointAddr)
                        do! SetupUserNameToken(proxy :?> IClientChannel)
                        return (new PtzAsync(proxy) :> IPtzAsync)
                })
                fun()->comp
        
            let GetEventClient =
                let comp = Async.Memoize(async{
                    dbg.Info(sprintf "%08X::%s" (sessionId.GetHashCode()) "GetEventClient")
                    let! eps = GetResolvedEndpoints()
                    match eps.EventsXAddr with
                    | None -> return null
                    | Some xaddr ->
                        do! Async.SwitchToThreadPool()
                        let! url = FixUrl(new Uri(xaddr, UriKind.RelativeOrAbsolute))
                        let useTls = url.Scheme = Uri.UriSchemeHttps
                        let! factory = getEventFactory(useTls)
                        let endpointAddr = new EndpointAddress(url)
                        let proxy = factory.CreateChannel(endpointAddr)
                        do! SetupUserNameToken(proxy :?> IClientChannel)
                        return (new EventAsync(proxy) :> IEventAsync)
                })
                fun()->comp

            let GetAnalyticsClient =
                let comp = Async.Memoize(async{
                    dbg.Info(sprintf "%08X::%s" (sessionId.GetHashCode()) "GetAnalyticsClient")
                    let! eps = GetResolvedEndpoints()
                    match eps.AnalyticsXAddr with
                    | None -> return null
                    | Some xaddr ->
                        do! Async.SwitchToThreadPool()
                        let! url = FixUrl(new Uri(xaddr, UriKind.RelativeOrAbsolute))
                        let useTls = url.Scheme = Uri.UriSchemeHttps
                        let! factory = getAnalyticsFactory(useTls)
                        let endpointAddr = new EndpointAddress(url)
                        let proxy = factory.CreateChannel(endpointAddr)
                        do! SetupUserNameToken(proxy :?> IClientChannel)
                        return (new AnalyticsAsync(proxy) :> IAnalyticsAsync)
                })
                fun()->comp
            
            let GetReceiverClient =
                let comp = Async.Memoize(async{
                    dbg.Info(sprintf "%08X::%s" (sessionId.GetHashCode()) "GetReceiverClient")
                    let! eps = GetResolvedEndpoints()
                    match eps.ReceiverXAddr with
                    | None -> return null
                    | Some xaddr ->
                        do! Async.SwitchToThreadPool()
                        let! url = FixUrl(new Uri(xaddr, UriKind.RelativeOrAbsolute))
                        let useTls = url.Scheme = Uri.UriSchemeHttps
                        let! factory = getReceiverFactory(useTls)
                        let endpointAddr = new EndpointAddress(url)
                        let proxy = factory.CreateChannel(endpointAddr)
                        do! SetupUserNameToken(proxy :?> IClientChannel)
                        return (new ReceiverAsync(proxy) :> IReceiverAsync)
                })
                fun()->comp
            
            let GetAnalyticsDeviceClient =
                let comp = Async.Memoize(async{
                    dbg.Info(sprintf "%08X::%s" (sessionId.GetHashCode()) "GetAnalyticsDeviceClient")
                    let! eps = GetResolvedEndpoints()
                    match eps.AnalyticsDeviceXAddr with
                    | None -> return null
                    | Some xaddr ->
                        do! Async.SwitchToThreadPool()
                        let! url = FixUrl(new Uri(xaddr, UriKind.RelativeOrAbsolute))
                        let useTls = url.Scheme = Uri.UriSchemeHttps
                        let! factory = getAnalyticsDeviceFactory(useTls)
                        let endpointAddr = new EndpointAddress(url)
                        let proxy = factory.CreateChannel(endpointAddr)
                        do! SetupUserNameToken(proxy :?> IClientChannel)
                        return (new AnalyticsDeviceAsync(proxy) :> IAnalyticsDeviceAsync)
                })
                fun()->comp

            let GetRecordingsClient =
                let comp = Async.Memoize(async{
                    dbg.Info(sprintf "%08X::%s" (sessionId.GetHashCode()) "GetRecordingsClient")
                    let! eps = GetResolvedEndpoints()
                    match eps.RecordingXAddr with
                    | None -> return null
                    | Some xaddr ->
                        do! Async.SwitchToThreadPool()
                        let! url = FixUrl(new Uri(xaddr, UriKind.RelativeOrAbsolute))
                        let useTls = url.Scheme = Uri.UriSchemeHttps
                        let! factory = getRecordingFactory(useTls)
                        let endpointAddr = new EndpointAddress(url)
                        let proxy = factory.CreateChannel(endpointAddr)
                        do! SetupUserNameToken(proxy :?> IClientChannel)
                        return (new RecordingAsync(proxy) :> IRecordingAsync)
                })
                fun()->comp

            let GetActionEngineClient : unit -> Async<IActionEngineAsync> =
                let comp = Async.Memoize(async {
                    dbg.Info(sprintf "%08X::%s" (sessionId.GetHashCode()) "GetActionEngineClient")
                    let! caps = GetCapabilities()
                    if caps.device.system.supportedVersions |> Seq.exists (fun v -> v >= OnvifVersion.v2_1) then
                        let! eps = GetResolvedEndpoints()
                        match eps.ActionEngineXAddr with
                        | None -> return null
                        | Some xaddr ->
                            do! Async.SwitchToThreadPool()
                            let! url = FixUrl(new Uri(xaddr, UriKind.RelativeOrAbsolute))
                            let useTls = url.Scheme = Uri.UriSchemeHttps
                            let! factory = getActionEngineFactory(useTls)
                            let endpointAddr = new EndpointAddress(url)
                            let proxy = factory.CreateChannel(endpointAddr)
                            do! SetupUserNameToken(proxy :?> IClientChannel)
                            return (new ActionEngineAsync(proxy) :> IActionEngineAsync)
                    else
                        return null
                })
                fun()->comp

            let GetReplayClient : unit -> Async<IReplayAsync> =
                let comp = Async.Memoize(async{
                    dbg.Info(sprintf "%08X::%s" (sessionId.GetHashCode()) "GetReplayClient")
                    let! eps = GetResolvedEndpoints()
                    match eps.ReplayXAddr with
                    | None -> return null
                    | Some xaddr ->
                        do! Async.SwitchToThreadPool()
                        let! url = FixUrl(new Uri(xaddr, UriKind.RelativeOrAbsolute))
                        let useTls = url.Scheme = Uri.UriSchemeHttps
                        let! factory = getReplayFactory(useTls)
                        let endpointAddr = new EndpointAddress(url)
                        let proxy = factory.CreateChannel(endpointAddr)
                        do! SetupUserNameToken(proxy :?> IClientChannel)
                        return (new ReplayAsync(proxy) :> IReplayAsync)
                })
                fun()->comp

            let GetAllCapabilities = 
                let comp = Async.Memoize(async{
                    let! dev = GetDeviceClient()
                    let! caps = dev.GetCapabilities()
                    try
                        let! ae = GetActionEngineClient()
                        let! aeCaps = ae.GetServiceCapabilities()
                        caps.actionEngine <- aeCaps
                    with
                        | err -> 
                            dbg.Error(err)
                    return caps
                })
                fun()->comp

            let GetMediaServiceCabalities =
                let comp = Async.Memoize(async{
                    let! media = GetMediaClient()
                    if media |> NotNull then
                        return! media.GetServiceCapabilities()
                    else
                        return null
                })
                fun() -> comp

            // Client for the ONVIF Media2 service (ver20/media/wsdl).
            // Media2 support is determined at session init via GetResolvedEndpoints(); returns null
            // if the camera does not advertise Media2 in GetServices().
            let GetMedia2Client =
                let comp = Async.Memoize(async{
                    dbg.Info(sprintf "%08X::%s" (sessionId.GetHashCode()) "GetMedia2Client")
                    let! eps = GetResolvedEndpoints()
                    if not eps.HasMedia2 then
                        return null
                    else
                        match eps.Media2XAddr with
                        | None -> return null
                        | Some xaddr ->
                            do! Async.SwitchToThreadPool()
                            let! url = FixUrl(new Uri(xaddr, UriKind.RelativeOrAbsolute))
                            let useTls = url.Scheme = Uri.UriSchemeHttps
                            let! factory = getMedia2Factory(useTls)
                            let endpointAddr = new EndpointAddress(url)
                            let proxy = factory.CreateChannel(endpointAddr)
                            do! SetupUserNameToken(proxy :?> IClientChannel)
                            return proxy
                })
                fun()->comp

            let MediaGetVideoSources = 
                let comp = Async.Memoize(async{
                    let! media = GetMediaClient()
                    if media |> NotNull then 
                        try
                            let! res = media.GetVideoSources()
                            return res |> SuppressNull [||]
                        with err->
                            dbg.Error(err)
                            return [||]
                    else
                        return [||]
                })
                fun() -> comp

            let MediaGetAudioSources = 
                let comp = Async.Memoize(async{
                    let! media = GetMediaClient()
                    if media |> NotNull then 
                        try
                            let! res = media.GetAudioSources()
                            return res |> SuppressNull [||]
                        with err->
                            dbg.Error(err)
                            return [||]
                    else
                        return [||]
                })
                fun() -> comp

            let MediaGetVideoAnalyticsConfigurationsImpl = 
                let prefComp() = async {
                    return 1
                }
                let altComp() = async {
                    return 0
                }
                let rec comp = 
                    let tramp = new Trampoline()
                    //let resolved = ref false
                    ref(fun()-> 
                        async{
                            try 
                                let! res = prefComp()
                                comp := prefComp
                                return res
                            with err->
                                comp := altComp
                                return! altComp()
                        }
                    )
                let rec comp = async{
                    try
                        let! med = GetMediaClient()
                        return! med.GetVideoAnalyticsConfigurations()
                    with
                        | :? FaultException as fault when (fault.Code.SubCode.Name) = "ActionNotSupported" && (fault.Code.SubCode.Namespace) = "http://www.onvif.org/ver10/error" ->
                            return [||]
                        | err ->
                            dbg.Error(err)
                            return raise err
                }
                fun() -> comp

            // ---------------------------------------------------------------
            // Media2 routing helpers — called by IMediaAsync implementations below
            // ---------------------------------------------------------------

            // Returns Profile[] parsed from a Media2 GetProfiles response.
            let getProfilesViaMedia2 (media2: IMedia2) = async {
                let request = new Media2GetProfilesRequest()
                let! response = Async.FromBeginEnd(request, media2.BeginGetProfiles, media2.EndGetProfiles)
                use response = response
                let bodyReader = response.GetReaderAtBodyContents()
                let bodyXml = bodyReader.ReadOuterXml()
                return Media2XmlParser.ParseGetProfilesResponse(bodyXml)
            }
            // Kick off endpoint resolution eagerly so service clients find results already cached
            do GetResolvedEndpoints() |> Async.Ignore |> Async.Start

            // Returns MediaUri parsed from a Media2 GetStreamUri response.
            let getStreamUriViaMedia2 (media2: IMedia2) (profileToken: string) = async {
                let request = new Media2GetStreamUriRequest()
                request.ProfileToken <- profileToken
                request.Protocol <- "RtspUnicast"
                let! response = Async.FromBeginEnd(request, media2.BeginGetStreamUri, media2.EndGetStreamUri)
                use response = response
                let bodyReader = response.GetReaderAtBodyContents()
                let bodyXml = bodyReader.ReadOuterXml()
                let uriStr = Media2XmlParser.ParseGetStreamUriResponse(bodyXml)
                let mediaUri = new MediaUri()
                if not(String.IsNullOrEmpty(uriStr)) then
                    let! fixedUri = FixUrl(new Uri(uriStr, UriKind.RelativeOrAbsolute))
                    mediaUri.uri <- fixedUri.OriginalString
                else
                    mediaUri.uri <- null
                return mediaUri
            }

            // Returns MediaUri parsed from a Media2 GetSnapshotUri response.
            let getSnapshotUriViaMedia2 (media2: IMedia2) (profileToken: string) = async {
                let request = new Media2GetSnapshotUriRequest()
                request.ProfileToken <- profileToken
                let! response = Async.FromBeginEnd(request, media2.BeginGetSnapshotUri, media2.EndGetSnapshotUri)
                use response = response
                let bodyReader = response.GetReaderAtBodyContents()
                let bodyXml = bodyReader.ReadOuterXml()
                let uriStr = Media2XmlParser.ParseGetStreamUriResponse(bodyXml)
                let mediaUri = new MediaUri()
                if not(String.IsNullOrEmpty(uriStr)) then
                    let! fixedUri = FixUrl(new Uri(uriStr, UriKind.RelativeOrAbsolute))
                    mediaUri.uri <- fixedUri.OriginalString
                else
                    mediaUri.uri <- null
                return mediaUri
            }

            // Returns VideoEncoderConfigurationOptions parsed from a Media2 GetVideoEncoderConfigurationOptions response.
            let getVideoEncoderConfigurationOptionsViaMedia2 (media2: IMedia2) (configToken: string) (profileToken: string) = async {
                let request = new Media2GetVideoEncoderConfigurationOptionsRequest()
                if not(String.IsNullOrEmpty(configToken)) then request.ConfigurationToken <- configToken
                if not(String.IsNullOrEmpty(profileToken)) then request.ProfileToken <- profileToken
                let! response = Async.FromBeginEnd(request, media2.BeginGetVideoEncoderConfigurationOptions, media2.EndGetVideoEncoderConfigurationOptions)
                use response = response
                let bodyReader = response.GetReaderAtBodyContents()
                let bodyXml = bodyReader.ReadOuterXml()
                return Media2XmlParser.ParseGetVideoEncoderConfigurationOptionsResponse(bodyXml)
            }

            // Returns VideoEncoderConfiguration[] parsed from a Media2 GetVideoEncoderConfigurations response.
            // profileToken: when non-empty, sends as filter so the camera returns only compatible configs.
            let getEncoderConfigurationsViaMedia2 (media2: IMedia2) (profileToken: string) = async {
                let request = new Media2GetVideoEncoderConfigurationsRequest()
                if not(String.IsNullOrEmpty(profileToken)) then request.ProfileToken <- profileToken
                let! response = Async.FromBeginEnd(request, media2.BeginGetVideoEncoderConfigurations, media2.EndGetVideoEncoderConfigurations)
                use response = response
                let bodyReader = response.GetReaderAtBodyContents()
                let bodyXml = bodyReader.ReadOuterXml()
                return Media2XmlParser.ParseGetVideoEncoderConfigurationsResponse(bodyXml)
            }

            // Returns VideoSourceConfiguration[] parsed from a Media2 GetVideoSourceConfigurations response.
            let getVideoSourceConfigurationsViaMedia2 (media2: IMedia2) = async {
                let request = new Media2GetVideoSourceConfigurationsRequest()
                let! response = Async.FromBeginEnd(request, media2.BeginGetVideoSourceConfigurations, media2.EndGetVideoSourceConfigurations)
                use response = response
                let bodyReader = response.GetReaderAtBodyContents()
                let bodyXml = bodyReader.ReadOuterXml()
                return Media2XmlParser.ParseGetVideoSourceConfigurationsResponse(bodyXml)
            }

            // Applies VideoEncoderConfiguration via Media2 SetVideoEncoderConfigurations.
            // ForcePersistence is not sent — Media2 does not support it.
            let setVideoEncoderConfigurationViaMedia2 (media2: IMedia2) (config: VideoEncoderConfiguration) = async {
                let request = new Media2SetVideoEncoderConfigurationsRequest()
                request.Configuration <- Media2XmlParser.BuildSetVideoEncoderConfigurationElement(config)
                let! response = Async.FromBeginEnd(request, media2.BeginSetVideoEncoderConfigurations, media2.EndSetVideoEncoderConfigurations)
                use response = response
                // Response body is empty for a successful Set — just consume it
                if not(response.IsEmpty) then
                    use _rdr = response.GetReaderAtBodyContents()
                    ()
            }

            {
                new INvtSession with
                    member this.deviceUri = deviceUri
                    member this.credentials = credentials
                    member this.CreatePullPointSubscription(filter:FilterType, initialTerminationTime:string, subscriptionPolicy:CreatePullPointSubscriptionSubscriptionPolicy) = async{
                        do! Async.SwitchToThreadPool()
                        let! evt = GetEventClient()
                        return! Async.CreateWithDisposable(fun sink->
                            Async.StartImmediate(async{
                                try
                                    let! pullpoint = evt.CreatePullPointSubscription(filter,initialTerminationTime, subscriptionPolicy, null)
                                    let! url = FixUrl(new Uri(pullpoint.SubscriptionReference.Address.Value))
                                    let refParams = 
                                        pullpoint |> IfNotNull (fun x->x.SubscriptionReference) |> IfNotNull (fun x->x.ReferenceParameters |> IfNotNull (fun x->x.Any)) |> SuppressNull [||]
                                    let addrHeaders = 
                                        refParams |> 
                                        Seq.map (fun rp-> 
                                            {new AddressHeader() with
                                                member this.Name = rp.LocalName
                                                member this.Namespace = rp.NamespaceURI
                                                member this.OnWriteAddressHeaderContents(writer) = 
                                                    rp.WriteContentTo(writer)
                                            }
                                        ) |> Seq.toArray
                                    let useTls = url.Scheme = Uri.UriSchemeHttps
                                    let! factory =  getSubManFactory(useTls)
                                    let endpointAddr = new EndpointAddress(url, addrHeaders)
                                    let proxy = factory.CreateChannel(endpointAddr)
                                    do! SetupUserNameToken(proxy :?> IClientChannel)
                                    let subman = new SubscriptionManagerAsync(proxy) :> ISubscriptionManagerAsync
                                    if not(sink.Success(subman)) then
                                        do! subman.Unsubscribe()
                                with err ->
                                    sink.Error(err) |> ignore
                            })
                            {new IDisposable with member d.Dispose() = sink.Cancel(new OperationCanceledException()) |> ignore}
                        )
                    }
                    member this.CreateBaseSubscription(consumerReference:EndpointReferenceType1, filter:FilterType, initialTerminationTime:string, subscriptionPolicy:SubscribeSubscriptionPolicy) = async{
                        let! evt = GetEventClient()
                        let! subscription = evt.Subscribe(consumerReference, filter, initialTerminationTime, subscriptionPolicy)
                        let submanUri = new Uri(subscription.SubscriptionReference.Address.Value)
                        let refParams = 
                            subscription |> IfNotNull (fun x->x.SubscriptionReference |> IfNotNull (fun x->x.ReferenceParameters |> IfNotNull (fun x->x.Any))) |> SuppressNull [||]
                        let addrHeaders = 
                            refParams |> 
                            Seq.map (fun rp-> 
                                {new AddressHeader() with
                                    member this.Name = rp.LocalName
                                    member this.Namespace = rp.NamespaceURI
                                    member this.OnWriteAddressHeaderContents(writer) = 
                                        rp.WriteContentTo(writer)
                                }
                            ) |> Seq.toArray
                        do! Async.SwitchToThreadPool()
                        let useTls = submanUri.Scheme = Uri.UriSchemeHttps
                        let! factory =  getSubManFactory(useTls)
                        let endpointAddr = new EndpointAddress(submanUri, addrHeaders)
                        let proxy = factory.CreateChannel(endpointAddr)
                        do! SetupUserNameToken(proxy :?> IClientChannel)
                        return new SubscriptionManagerAsync(proxy) :> ISubscriptionManagerAsync
                    }

                    member this.GetAllCapabilities() =
                        GetAllCapabilities()



                end

                interface IDeviceAsync with
                    member this.GetDeviceInformation() = 
                        GetDeviceInformation()
                          
                    member this.GetCapabilities(categories:CapabilityCategory[]) = 
                        GetCapabilities()

                    member this.GetScopes() = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetScopes()
                    }
            
                    member this.SetScopes(scopes:string[]) = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetScopes(scopes)
                    }
            
                    member this.GetNetworkInterfaces() = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetNetworkInterfaces()
                    }
        
                    member this.SetNetworkInterfaces(token:string, networkInterface:NetworkInterfaceSetConfiguration) = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetNetworkInterfaces(token, networkInterface)
                    }

                    member this.GetNetworkDefaultGateway() = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetNetworkDefaultGateway()
                    }
            
                    member this.SetNetworkDefaultGateway(ipv4Addresses:string[], ipv6Addresses:string[]) = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetNetworkDefaultGateway(ipv4Addresses, ipv6Addresses)
                        return ()
                    }

                    member this.GetDNS() = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetDNS()
                    }

                    member this.SetDNS(fromDHCP:bool, searchDomain:string[], dnsManual:IPAddress[]) = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetDNS(fromDHCP, searchDomain, dnsManual)
                    }

                    member this.GetSystemDateAndTime() = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetSystemDateAndTime()
                    }

                    member this.SetSystemDateAndTime(dateTimeType:SetDateTimeType, daylightSavings:bool, timeZone:TimeZone , utcDateTime:DateTime) = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetSystemDateAndTime(dateTimeType, daylightSavings, timeZone, utcDateTime)
                    }

                    member this.GetNTP() = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetNTP()
                    }

                    member this.SetNTP(fromDhcp: bool, ntpManual: NetworkHost[]) = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetNTP(fromDhcp, ntpManual)
                    }

                    member this.GetUsers() = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetUsers()
                    }

                    member this.CreateUsers(users: User[]) = async{
                        let! dev = GetDeviceClient()
                        return! dev.CreateUsers(users)
                    }

                    member this.DeleteUsers(userNames:string[]) = async{
                        let! dev = GetDeviceClient()
                        return! dev.DeleteUsers(userNames)
                    }
            
                    member this.SetUser(users: User[]) = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetUser(users)
                    }
        
                    member this.SystemReboot() = async{
                        let! dev = GetDeviceClient()
                        return! dev.SystemReboot()
                    }

                    member this.SetSystemFactoryDefault(factoryDefault:FactoryDefaultType) = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetSystemFactoryDefault(factoryDefault)
                    }

                    member this.GetSystemLog(logType:SystemLogType) = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetSystemLog(logType)
                    }
            
                    member this.UpgradeSystemFirmware(firmware:byte[]) = async{
                        let! dev = GetDeviceMtomClient()
                        return! dev.UpgradeSystemFirmware(firmware)
                    }

                    member this.StartFirmwareUpgrade() = async{
                        let! dev = GetDeviceClient()
                        let! response = dev.StartFirmwareUpgrade()
                        let! uploadUrl = FixUrl(new Uri(response.UploadUri))
                        response.UploadUri <- uploadUrl.OriginalString
                        return response
                    }

                    member this.RestoreSystem(backupFiles:BackupFile[]) = async{
                        let! dev = GetDeviceMtomClient()
                        return! dev.RestoreSystem(backupFiles)
                    }

                    member this.GetSystemBackup() = async{
                        let! dev = GetDeviceMtomClient()
                        return! dev.GetSystemBackup()
                    }

                    member this.GetSystemSupportInformation() = async{
                        let! dev = GetDeviceMtomClient()
                        return! dev.GetSystemSupportInformation()
                    }

                    member this.CreateCertificate(certificateID:string, subject:string, validNotBefore:System.DateTime, validNotAfter:System.DateTime) = async{
                        let! dev = GetDeviceClient()
                        return! dev.CreateCertificate(certificateID, subject, validNotBefore, validNotAfter)
                    }

                    member this.GetCertificates() = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetCertificates()
                    }

                    member this.GetCertificatesStatus() = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetCertificatesStatus()
                    }

                    member this.SetCertificatesStatus(certificateStatuses:CertificateStatus[]) = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetCertificatesStatus(certificateStatuses)
                    }

                    member this.DeleteCertificates(certificateIds:string[]) = async{
                        let! dev = GetDeviceClient()
                        return! dev.DeleteCertificates(certificateIds)
                    }

                    member this.LoadCertificates(certificates:Certificate[]) = async{
                        let! dev = GetDeviceClient()
                        return! dev.LoadCertificates(certificates)
                    }

                    member this.GetClientCertificateMode() = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetClientCertificateMode()
                    }

                    member this.SetClientCertificateMode(enabled:bool) = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetClientCertificateMode(enabled)
                    }

                    member this.GetPkcs10Request(certificateID:string, subject:string, attributes:BinaryData) = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetPkcs10Request(certificateID, subject, attributes)
                    }

                    member this.AddScopes(scopeItems:string[]):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.AddScopes(scopeItems)
                    }
                    member this.RemoveScopes(scopeItems:string[]):Async<string[]> = async{
                        let! dev = GetDeviceClient()
                        return! dev.RemoveScopes(scopeItems)
                    }
                    member this.GetDiscoveryMode():Async<DiscoveryMode> = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetDiscoveryMode()
                    }
                    member this.SetDiscoveryMode(mode:DiscoveryMode):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetDiscoveryMode(mode)
                    }
                    member this.GetRemoteDiscoveryMode():Async<DiscoveryMode> = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetRemoteDiscoveryMode()
                    }
                    member this.SetRemoteDiscoveryMode(mode:DiscoveryMode):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetRemoteDiscoveryMode(mode)
                    }
                    member this.GetDPAddresses():Async<NetworkHost[]> = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetDPAddresses()
                    }
                    member this.SetDPAddresses(addresses:NetworkHost[]):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetDPAddresses(addresses)
                    }
                    member this.GetHostname():Async<HostnameInformation> = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetHostname()
                    }
                    member this.SetHostname(name:string):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetHostname(name)
                    }
                    member this.GetDynamicDNS():Async<DynamicDNSInformation> = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetDynamicDNS()
                    }
                    member this.SetDynamicDNS(dynDnsType:DynamicDNSType, name:string, ttl:XsDuration):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetDynamicDNS(dynDnsType, name, ttl)
                    }
                    member this.GetNetworkProtocols():Async<NetworkProtocol[]> = async{
                        let! dev = GetDeviceClient()
                        try
                            let! res = dev.GetNetworkProtocols()
                            return res |> SuppressNull [||]
                        with err ->
                            dbg.Error(err)
                            return [||]
                    }
                    member this.SetNetworkProtocols(protocols:NetworkProtocol[]):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetNetworkProtocols(protocols)
                    }
                    member this.GetZeroConfiguration():Async<NetworkZeroConfiguration> = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetZeroConfiguration()
                    }
                    member this.SetZeroConfiguration(nicToken:string, enabled:bool):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetZeroConfiguration(nicToken, enabled)
                    }
                    member this.GetIPAddressFilter():Async<IPAddressFilter> = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetIPAddressFilter()
                    }
                    member this.SetIPAddressFilter(filter:IPAddressFilter):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetIPAddressFilter(filter)
                    }
                    member this.AddIPAddressFilter(filter:IPAddressFilter):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.AddIPAddressFilter(filter)
                    }
                    member this.RemoveIPAddressFilter(filter:IPAddressFilter):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.RemoveIPAddressFilter(filter)
                    }
                    member this.GetAccessPolicy():Async<BinaryData> = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetAccessPolicy()
                    }
                    member this.SetAccessPolicy(policyFile:BinaryData):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetAccessPolicy(policyFile)
                    }
                    member this.GetRelayOutputs():Async<RelayOutput[]> = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetRelayOutputs()
                    }
                    member this.SetRelayOutputSettings(token:string, settings:RelayOutputSettings):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetRelayOutputSettings(token, settings)
                    }
                    member this.SetRelayOutputState(token:string, state:RelayLogicalState):Async<unit> = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetRelayOutputState(token, state)
                    }
                    member this.SetHostnameFromDHCP(fromDhcp:bool):Async<bool> = async{
                        let! dev = GetDeviceClient()
                        return! dev.SetHostnameFromDHCP(fromDhcp)
                    }
                    //----------------------------------------------------------------------------------
                    //onvif 2.1
                    //----------------------------------------------------------------------------------
                    member this.GetWsdlUrl():Async<string> = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetWsdlUrl()
                    }
                    member this.GetServices(includeCapability:bool):Async<Service[]> = async{
                        let! dev = GetDeviceClient()
                        return! dev.GetServices(includeCapability)
                    }
                    member this.GetServiceCapabilities():Async<DeviceServiceCapabilities1> = 
                        GetServiceCapabilities()
                end

                interface IMediaAsync with
                    member this.GetVideoSources() = MediaGetVideoSources()

                    member this.GetAudioSources() = MediaGetAudioSources()

                    member this.GetServiceCapabilities() = GetMediaServiceCabalities()

                    member this.CreateProfile(name:String, token:string) = async{
                        let! med = GetMediaClient()
                        return! med.CreateProfile(name, token)
                    }

                    member this.GetProfiles() = async{
                        let! media2 = GetMedia2Client()
                        if media2 |> NotNull then
                            try
                                return! getProfilesViaMedia2 media2
                            with err ->
                                dbg.Error(err)
                                let! med = GetMediaClient()
                                if med |> NotNull then
                                    return! med.GetProfiles()
                                else
                                    return [||]
                        else
                            let! med = GetMediaClient()
                            if med |> NotNull then
                                return! med.GetProfiles()
                            else
                                return [||]
                    }

                    member this.GetProfile(profileToken:string) = async{
                        let! med = GetMediaClient()
                        return! med.GetProfile(profileToken)
                    }

                    member this.DeleteProfile(token:string) = async{
                        let! med = GetMediaClient()
                        return! med.DeleteProfile(token)
                    }

                    member this.GetStreamUri(streamSetup:StreamSetup, token:string) = async{
                        let! media2 = GetMedia2Client()
                        if media2 |> NotNull then
                            try
                                return! getStreamUriViaMedia2 media2 token
                            with err ->
                                dbg.Error(err)
                                let! med = GetMediaClient()
                                let! mediaUri = med.GetStreamUri(streamSetup, token)
                                let! fixedMediaUrl = FixUrl(new Uri(mediaUri.uri))
                                mediaUri.uri <- fixedMediaUrl.OriginalString
                                return mediaUri
                        else
                            let! med = GetMediaClient()
                            let! mediaUri = med.GetStreamUri(streamSetup, token)
                            let! fixedMediaUrl = FixUrl(new Uri(mediaUri.uri))
                            mediaUri.uri <- fixedMediaUrl.OriginalString
                            return mediaUri
                    }

                    member this.GetSnapshotUri(token:string) = async{
                        let! media2 = GetMedia2Client()
                        if media2 |> NotNull then
                            try
                                return! getSnapshotUriViaMedia2 media2 token
                            with err ->
                                dbg.Error(err)
                                let! med = GetMediaClient()
                                let! mediaUri = med.GetSnapshotUri(token)
                                if mediaUri |> NotNull then
                                    if not(String.IsNullOrEmpty(mediaUri.uri)) then
                                        let! fixedMediaUrl = FixUrl(new Uri(mediaUri.uri, UriKind.RelativeOrAbsolute))
                                        mediaUri.uri <- fixedMediaUrl.OriginalString
                                    else
                                        mediaUri.uri <- null
                                return mediaUri
                        else
                            let! med = GetMediaClient()
                            let! mediaUri = med.GetSnapshotUri(token)
                            if mediaUri |> NotNull then
                                if not(String.IsNullOrEmpty(mediaUri.uri)) then
                                    let! fixedMediaUrl = FixUrl(new Uri(mediaUri.uri, UriKind.RelativeOrAbsolute))
                                    mediaUri.uri <- fixedMediaUrl.OriginalString
                                else
                                    mediaUri.uri <- null
                            return mediaUri
                    }

                    member this.AddVideoEncoderConfiguration(profToken:string, cofigToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.AddVideoEncoderConfiguration(profToken, cofigToken)
                    }

                    member this.RemoveVideoEncoderConfiguration(profToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.RemoveVideoEncoderConfiguration(profToken)
                    }

                    member this.AddVideoSourceConfiguration(profToken:string, cofigToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.AddVideoSourceConfiguration(profToken, cofigToken)
                    }

                    member this.RemoveVideoSourceConfiguration(profToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.RemoveVideoSourceConfiguration(profToken)
                    }

                    member this.AddAudioEncoderConfiguration(profToken:string, cofigToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.AddAudioEncoderConfiguration(profToken, cofigToken)
                    }

                    member this.RemoveAudioEncoderConfiguration(profToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.RemoveAudioEncoderConfiguration(profToken)
                    }

                    member this.AddAudioSourceConfiguration(profToken:string, cofigToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.AddAudioSourceConfiguration(profToken, cofigToken)
                    }

                    member this.RemoveAudioSourceConfiguration(profToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.RemoveAudioSourceConfiguration(profToken)
                    }

                    member this.AddPTZConfiguration(profToken:string, cofigToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.AddPTZConfiguration(profToken, cofigToken)
                    }

                    member this.RemovePTZConfiguration(profToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.RemovePTZConfiguration(profToken)
                    }

                    member this.AddVideoAnalyticsConfiguration(profToken:string, cofigToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.AddVideoAnalyticsConfiguration(profToken, cofigToken)
                    }

                    member this.RemoveVideoAnalyticsConfiguration(profToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.RemoveVideoAnalyticsConfiguration(profToken)
                    }

                    member this.AddMetadataConfiguration(profToken:string, cofigToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.AddMetadataConfiguration(profToken, cofigToken)
                    }

                    member this.RemoveMetadataConfiguration(profToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.RemoveMetadataConfiguration(profToken)
                    }

                    member this.GetVideoSourceConfigurations(): Async<VideoSourceConfiguration[]> = async{
                        let! media2 = GetMedia2Client()
                        if media2 |> NotNull then
                            try
                                return! getVideoSourceConfigurationsViaMedia2 media2
                            with _ ->
                                let! med = GetMediaClient()
                                if med |> NotNull then return! med.GetVideoSourceConfigurations()
                                else return [||]
                        else
                            let! med = GetMediaClient()
                            if med |> NotNull then
                                return! med.GetVideoSourceConfigurations()
                            else
                                return [||]
                    }

                    member this.GetVideoEncoderConfigurations(): Async<VideoEncoderConfiguration[]> = async{
                        let! media2 = GetMedia2Client()
                        if media2 |> NotNull then
                            try
                                return! getEncoderConfigurationsViaMedia2 media2 ""
                            with _ ->
                                let! med = GetMediaClient()
                                if med |> NotNull then
                                    try
                                        return! med.GetVideoEncoderConfigurations()
                                    with
                                        | :? FaultException as fault when (fault.Code.SubCode.Name) = "ActionNotSupported" && (fault.Code.SubCode.Namespace) = "http://www.onvif.org/ver10/error" ->
                                            return [||]
                                        | err ->
                                            dbg.Error(err)
                                            return raise err
                                else
                                    return [||]
                        else
                            let! med = GetMediaClient()
                            if med |> NotNull then
                                try
                                    return! med.GetVideoEncoderConfigurations()
                                with
                                    | :? FaultException as fault when (fault.Code.SubCode.Name) = "ActionNotSupported" && (fault.Code.SubCode.Namespace) = "http://www.onvif.org/ver10/error" ->
                                        return [||]
                                    | err ->
                                        dbg.Error(err)
                                        return raise err
                            else
                                return [||]
                    }

                    member this.GetAudioSourceConfigurations(): Async<AudioSourceConfiguration[]> = async{
                        let! med = GetMediaClient()
                        if med |> NotNull then
                            return! med.GetAudioSourceConfigurations()
                        else
                            return [||]
                    }

                    member this.GetAudioEncoderConfigurations(): Async<AudioEncoderConfiguration[]> = async{
                        let! med = GetMediaClient()
                        if med |> NotNull then
                            try
                                return! med.GetAudioEncoderConfigurations()
                            with 
                                | :? FaultException as fault when (fault.Code.SubCode.Name) = "ActionNotSupported" && (fault.Code.SubCode.Namespace) = "http://www.onvif.org/ver10/error" ->
                                    return [||]
                                | err -> 
                                    dbg.Error(err)
                                    return raise err
                        else
                            return [||]
                    }

                    member this.GetVideoAnalyticsConfigurations(): Async<VideoAnalyticsConfiguration[]> = async{
                        let! med = GetMediaClient()
                        if med |> NotNull then
                            try
                                return! med.GetVideoAnalyticsConfigurations()
                            with
                                | :? FaultException as fault when (fault.Code.SubCode.Name) = "ActionNotSupported" && (fault.Code.SubCode.Namespace) = "http://www.onvif.org/ver10/error" ->
                                    return [||]
                                | err ->
                                    dbg.Error(err)
                                    return raise err
                        else
                            return [||]
                    }

                    member this.GetMetadataConfigurations(): Async<MetadataConfiguration[]> = async{
                        let! med = GetMediaClient()
                        if med |> NotNull then
                            try
                                return! med.GetMetadataConfigurations()
                            with 
                                | :? FaultException as fault when (fault.Code.SubCode.Name) = "ActionNotSupported" && (fault.Code.SubCode.Namespace) = "http://www.onvif.org/ver10/error" ->
                                    return [||]
                                | err -> 
                                    dbg.Error(err)
                                    return raise err
                        else
                            return [||]
                    }

                    member this.GetVideoSourceConfiguration(token:string): Async<VideoSourceConfiguration> = async{
                        let! med = GetMediaClient()
                        return! med.GetVideoSourceConfiguration(token)
                    }

                    member this.GetVideoEncoderConfiguration(token:string): Async<VideoEncoderConfiguration> = async{
                        let! med = GetMediaClient()
                        return! med.GetVideoEncoderConfiguration(token)
                    }

                    member this.GetAudioSourceConfiguration(token:string): Async<AudioSourceConfiguration> = async{
                        let! med = GetMediaClient()
                        return! med.GetAudioSourceConfiguration(token)
                    }

                    member this.GetAudioEncoderConfiguration(token:string): Async<AudioEncoderConfiguration> = async{
                        let! med = GetMediaClient()
                        return! med.GetAudioEncoderConfiguration(token)
                    }

                    member this.GetVideoAnalyticsConfiguration(token:string): Async<VideoAnalyticsConfiguration> = async{
                        let! med = GetMediaClient()
                        return! med.GetVideoAnalyticsConfiguration(token)
                    }

                    member this.GetMetadataConfiguration(token:string): Async<MetadataConfiguration> = async{
                        let! med = GetMediaClient()
                        return! med.GetMetadataConfiguration(token)
                    }

                    member this.GetCompatibleVideoEncoderConfigurations(profToken:string): Async<VideoEncoderConfiguration[]> = async{
                        let! media2 = GetMedia2Client()
                        if media2 |> NotNull then
                            try
                                return! getEncoderConfigurationsViaMedia2 media2 profToken
                            with _ ->
                                let! med = GetMediaClient()
                                try
                                    return! med.GetCompatibleVideoEncoderConfigurations(profToken)
                                with
                                    | :? FaultException as fault when (fault.Code.SubCode.Name) = "ActionNotSupported" && (fault.Code.SubCode.Namespace) = "http://www.onvif.org/ver10/error" ->
                                        return! this.GetVideoEncoderConfigurations()
                                    | err ->
                                        dbg.Error(err)
                                        return raise err
                        else
                            let! med = GetMediaClient()
                            try
                                return! med.GetCompatibleVideoEncoderConfigurations(profToken)
                            with
                                | :? FaultException as fault when (fault.Code.SubCode.Name) = "ActionNotSupported" && (fault.Code.SubCode.Namespace) = "http://www.onvif.org/ver10/error" ->
                                    return! this.GetVideoEncoderConfigurations()
                                | err ->
                                    dbg.Error(err)
                                    return raise err
                    }

                    member this.GetCompatibleVideoSourceConfigurations(profToken:string): Async<VideoSourceConfiguration[]> = async{
                        let! med = GetMediaClient()
                        return! med.GetCompatibleVideoSourceConfigurations(profToken)
                    }

                    member this.GetCompatibleAudioEncoderConfigurations(profToken:string): Async<AudioEncoderConfiguration[]> = async{
                        let! med = GetMediaClient()
                        try
                            return! med.GetCompatibleAudioEncoderConfigurations(profToken)
                        with 
                            | :? FaultException as fault when (fault.Code.SubCode.Name) = "ActionNotSupported" && (fault.Code.SubCode.Namespace) = "http://www.onvif.org/ver10/error" ->
                                return! this.GetAudioEncoderConfigurations()
                            | err -> 
                                dbg.Error(err)
                                return raise err
                    }

                    member this.GetCompatibleAudioSourceConfigurations(profToken:string): Async<AudioSourceConfiguration[]> = async{
                        let! med = GetMediaClient()
                        return! med.GetCompatibleAudioSourceConfigurations(profToken)
                    }

                    member this.GetCompatibleVideoAnalyticsConfigurations(profToken:string): Async<VideoAnalyticsConfiguration[]> = async{
                        let! med = GetMediaClient()
                        try
                            return! med.GetCompatibleVideoAnalyticsConfigurations(profToken)
                        with 
                            | :? FaultException as fault when (fault.Code.SubCode.Name) = "ActionNotSupported" && (fault.Code.SubCode.Namespace) = "http://www.onvif.org/ver10/error" ->
                                return! this.GetVideoAnalyticsConfigurations()
                            | err -> 
                                dbg.Error(err)
                                return raise err
                    }

                    member this.GetCompatibleMetadataConfigurations(profToken:string): Async<MetadataConfiguration[]> = async{
                        let! med = GetMediaClient()
                        try
                            return! med.GetCompatibleMetadataConfigurations(profToken)
                        with 
                            | :? FaultException as fault when (fault.Code.SubCode.Name) = "ActionNotSupported" && (fault.Code.SubCode.Namespace) = "http://www.onvif.org/ver10/error" ->
                                return! this.GetMetadataConfigurations()
                            | err -> 
                                dbg.Error(err)
                                return raise err
                    }

                    member this.SetVideoSourceConfiguration(config:VideoSourceConfiguration, forcePersistence:bool): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.SetVideoSourceConfiguration(config, forcePersistence)
                    }

                    member this.SetVideoEncoderConfiguration(config:VideoEncoderConfiguration, forcePersistence:bool): Async<unit> = async{
                        let! media2 = GetMedia2Client()
                        if media2 |> NotNull then
                            try
                                return! setVideoEncoderConfigurationViaMedia2 media2 config
                            with err ->
                                dbg.Error(err)
                                let! med = GetMediaClient()
                                return! med.SetVideoEncoderConfiguration(config, forcePersistence)
                        else
                            let! med = GetMediaClient()
                            return! med.SetVideoEncoderConfiguration(config, forcePersistence)
                    }

                    member this.SetAudioSourceConfiguration(config:AudioSourceConfiguration, forcePersistence:bool): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.SetAudioSourceConfiguration(config, forcePersistence)
                    }

                    member this.SetAudioEncoderConfiguration(config:AudioEncoderConfiguration, forcePersistence:bool): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.SetAudioEncoderConfiguration(config, forcePersistence)
                    }

                    member this.SetVideoAnalyticsConfiguration(config:VideoAnalyticsConfiguration, forcePersistence:bool): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.SetVideoAnalyticsConfiguration(config, forcePersistence)
                    }

                    member this.SetMetadataConfiguration(config:MetadataConfiguration, forcePersistence:bool): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.SetMetadataConfiguration(config, forcePersistence)
                    }

                    member this.GetVideoSourceConfigurationOptions(configToken:string, profToken:string): Async<VideoSourceConfigurationOptions> = async{
                        let! med = GetMediaClient()
                        return! med.GetVideoSourceConfigurationOptions(configToken, profToken)
                    }

                    member this.GetVideoEncoderConfigurationOptions(configToken:string, profToken:string): Async<VideoEncoderConfigurationOptions> = async{
                        let! media2 = GetMedia2Client()
                        if media2 |> NotNull then
                            try
                                return! getVideoEncoderConfigurationOptionsViaMedia2 media2 configToken profToken
                            with err ->
                                dbg.Error(err)
                                let! med = GetMediaClient()
                                return! med.GetVideoEncoderConfigurationOptions(configToken, profToken)
                        else
                            let! med = GetMediaClient()
                            return! med.GetVideoEncoderConfigurationOptions(configToken, profToken)
                    }

                    member this.GetAudioSourceConfigurationOptions(configToken:string, profToken:string): Async<AudioSourceConfigurationOptions> = async{
                        let! med = GetMediaClient()
                        return! med.GetAudioSourceConfigurationOptions(configToken, profToken)
                    }

                    member this.GetAudioEncoderConfigurationOptions(configToken:string, profToken:string): Async<AudioEncoderConfigurationOptions> = async{
                        let! med = GetMediaClient()
                        return! med.GetAudioEncoderConfigurationOptions(configToken, profToken)
                    }

                    member this.GetMetadataConfigurationOptions(configToken:string, profToken:string): Async<MetadataConfigurationOptions> = async{
                        let! med = GetMediaClient()
                        return! med.GetMetadataConfigurationOptions(configToken, profToken)
                    }

                    member this.GetGuaranteedNumberOfVideoEncoderInstances(vscToken:string): Async<GetGuaranteedNumberOfVideoEncoderInstancesResponse> = async{
                        let! med = GetMediaClient()
                        return! med.GetGuaranteedNumberOfVideoEncoderInstances(vscToken)
                    }

                    member this.StartMulticastStreaming(profToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.StartMulticastStreaming(profToken)
                    }

                    member this.StopMulticastStreaming(profToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.StopMulticastStreaming(profToken)
                    }

                    member this.SetSynchronizationPoint(profToken:string): Async<unit> = async{
                        let! med = GetMediaClient()
                        return! med.SetSynchronizationPoint(profToken)
                    }
                end

                interface IImagingAsync with
                    member this.GetImagingSettings(videoSourceToken:string):Async<ImagingSettings20> = async{
                        let! img = GetImagingClient()
                        return! img.GetImagingSettings(videoSourceToken)
                    }

                    member this.SetImagingSettings(videoSourceToken:string, imagingSettings:ImagingSettings20, forcePersistence:bool):Async<unit> = async{
                        let! img = GetImagingClient()
                        return! img.SetImagingSettings(videoSourceToken, imagingSettings, forcePersistence)
                    }

                    member this.GetOptions(videoSourceToken:string):Async<ImagingOptions20> = async{
                        let! img = GetImagingClient()
                        return! img.GetOptions(videoSourceToken)
                    }

                    member this.Move(videoSourceToken:string, focus:FocusMove):Async<unit> = async{
                        let! img = GetImagingClient()
                        return! img.Move(videoSourceToken, focus)
                    }

                    member this.GetMoveOptions(videoSourceToken:string):Async<MoveOptions20> = async{
                        let! img = GetImagingClient()
                        return! img.GetMoveOptions(videoSourceToken)
                    }

                    member this.Stop(videoSourceToken:string):Async<unit> = async{
                        let! img = GetImagingClient()
                        return! img.Stop(videoSourceToken)
                    }

                    member this.GetStatus(videoSourceToken:string):Async<ImagingStatus20> = async{
                        let! img = GetImagingClient()
                        return! img.GetStatus(videoSourceToken)
                    }
                end


                interface IPtzAsync with
                    member this.GetNodes() = async{
                        let! ptz = GetPtzClient()
                        return! ptz.GetNodes()
                    }

                    member this.GetNode(nodeToken:string) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.GetNode(nodeToken)
                    }

                    member this.GetConfiguration(configurationToken:string) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.GetConfiguration(configurationToken)
                    }

                    member this.GetConfigurations() = async{
                        let! ptz = GetPtzClient()
                        try
                            return! ptz.GetConfigurations()
                        with 
                            | :? FaultException as fault when (fault.Code.SubCode.Name) = "ActionNotSupported" && (fault.Code.SubCode.Namespace) = "http://www.onvif.org/ver10/error" ->
                                return [||]
                            | err -> 
                                dbg.Error(err)
                                return raise err
                    }

                    member this.SetConfiguration(ptzConfiguration:PTZConfiguration, forcePersistance:bool) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.SetConfiguration(ptzConfiguration, forcePersistance)
                    }

                    member this.GetConfigurationOptions(configurationToken:string) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.GetConfigurationOptions(configurationToken)
                    }

                    member this.SendAuxiliaryCommand(profileToken:string, auxiliaryData:string) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.SendAuxiliaryCommand(profileToken, auxiliaryData)
                    }

                    member this.GetPresets(profileToken:string) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.GetPresets(profileToken)
                    }

                    member this.SetPreset(profileToken:string, presetName:string, presetToken:string) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.SetPreset(profileToken, presetName, presetToken)
                    }

                    member this.RemovePreset(profileToken:string, presetToken:string) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.RemovePreset(profileToken, presetToken)
                    }

                    member this.GotoPreset(profileToken:string, presetToken:string, speed:PTZSpeed) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.GotoPreset(profileToken, presetToken, speed)
                    }

                    member this.GotoHomePosition(profileToken:string, speed:PTZSpeed) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.GotoHomePosition(profileToken, speed)
                    }

                    member this.SetHomePosition(profileToken:string) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.SetHomePosition(profileToken)
                    }

                    member this.ContinuousMove(profileToken:string, velocity:PTZSpeed, timeout:XsDuration) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.ContinuousMove(profileToken, velocity, timeout)
                    }

                    member this.RelativeMove(profileToken:string, translation:PTZVector, speed:PTZSpeed) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.RelativeMove(profileToken, translation, speed)
                    }

                    member this.GetStatus(profileToken:string) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.GetStatus(profileToken)
                    }

                    member this.AbsoluteMove(profileToken:string, position:PTZVector, speed:PTZSpeed) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.AbsoluteMove(profileToken, position, speed)
                    }

                    member this.Stop(profileToken:string, panTilt:bool, zoom:bool) = async{
                        let! ptz = GetPtzClient()
                        return! ptz.Stop(profileToken, panTilt, zoom)
                    }
                end

                interface IEventAsync with
                    member this.CreatePullPointSubscription(filter:FilterType, initialTerminationTime:string, subscriptionPolicy:CreatePullPointSubscriptionSubscriptionPolicy, any:XmlElement[]): Async<CreatePullPointSubscriptionResponse> = async{
                        let! evt = GetEventClient()
                        return! evt.CreatePullPointSubscription(filter, initialTerminationTime, subscriptionPolicy, any)
                    }
                    member this.GetEventProperties(): Async<GetEventPropertiesResponse> = async{
                        let! evt = GetEventClient()
                        return! evt.GetEventProperties()
                    }
                    member this.Subscribe(consumerReference:EndpointReferenceType1, filter:FilterType, initialTerminationTime:string, subscriptionPolicy:SubscribeSubscriptionPolicy): Async<SubscribeResponse> = async{
                        let! evt = GetEventClient()
                        return! evt.Subscribe(consumerReference, filter, initialTerminationTime, subscriptionPolicy)
                    }
                    member this.GetCurrentMessage(topic:TopicExpressionType): Async<unit> = async{
                        let! evt = GetEventClient()
                        return! evt.GetCurrentMessage(topic)
                    }
                end

                interface IAnalyticsEngineAsync with
                    member this.GetSupportedAnalyticsModules(vacToken:string):Async<SupportedAnalyticsModules> = async{
                        let! client = GetAnalyticsClient()
                        let! moduleTypes = client.GetSupportedAnalyticsModules(vacToken)
                        if moduleTypes.analyticsModuleContentSchemaLocation |> NotNull then
                            let! schemaUrls = Async.Parallel(seq{
                                for url in moduleTypes.analyticsModuleContentSchemaLocation do
                                    yield async{
                                        let! fixedUrl = FixUrl(new Uri(url))
                                        return fixedUrl.ToString()
                                    }
                            })
                            moduleTypes.analyticsModuleContentSchemaLocation <- schemaUrls
                        return moduleTypes
                    }
                    member this.CreateAnalyticsModules(vacToken:string, analyticsModules:Config[]):Async<unit> = async{
                        let! client = GetAnalyticsClient()
                        return! client.CreateAnalyticsModules(vacToken, analyticsModules)
                    }
                    member this.DeleteAnalyticsModules(vacToken:string, analyticsModuleNames:string[]):Async<unit> = async{
                        let! client = GetAnalyticsClient()
                        return! client.DeleteAnalyticsModules(vacToken, analyticsModuleNames)
                    }
                    member this.GetAnalyticsModules(vacToken:string):Async<Config[]> = async{
                        let! client = GetAnalyticsClient()
                        return! client.GetAnalyticsModules(vacToken)
                    }
                    member this.ModifyAnalyticsModules(vacToken:string, analyticsModules:Config[]):Async<unit> = async{
                        let! client = GetAnalyticsClient()
                        return! client.ModifyAnalyticsModules(vacToken, analyticsModules)
                    }
                end

                interface IRuleEngineAsync with
                    member this.GetSupportedRules(vacToken:string):Async<SupportedRules> = async{
                        let! client = GetAnalyticsClient()
                        let! ruleTypes = client.GetSupportedRules(vacToken)
                        if ruleTypes.ruleContentSchemaLocation |> NotNull then
                            let! schemaUrls = Async.Parallel(seq{
                                for url in ruleTypes.ruleContentSchemaLocation do
                                    yield async{
                                        let! fixedUrl = FixUrl(new Uri(url))
                                        return fixedUrl.ToString()
                                    }
                            })
                            ruleTypes.ruleContentSchemaLocation <- schemaUrls
                        return ruleTypes
                    }
                    member this.CreateRules(vacToken:string, rules:Config[]):Async<unit> = async{
                        let! client = GetAnalyticsClient()
                        return! client.CreateRules(vacToken, rules)
                    }
                    member this.DeleteRules(vacToken:string, ruleNames:string[]):Async<unit> = async{
                        let! client = GetAnalyticsClient()
                        return! client.DeleteRules(vacToken, ruleNames)
                    }
                    member this.GetRules(vacToken:string):Async<Config[]> = async{
                        let! client = GetAnalyticsClient()
                        return! client.GetRules(vacToken)
                    }
                    member this.ModifyRules(vacToken:string, rules:Config[]):Async<unit> = async{
                        let! client = GetAnalyticsClient()
                        return! client.ModifyRules(vacToken, rules)
                    }
                end

                interface IReceiverAsync with
                    member this.GetServiceCapabilities():Async<Capabilities11> = async{
                        let! client = GetReceiverClient()
                        return! client.GetServiceCapabilities()
                    }
                    member this.GetReceivers():Async<Receiver[]> = async{
                        let! client = GetReceiverClient()
                        return! client.GetReceivers()
                    }
                    member this.GetReceiver(receiverToken:string):Async<Receiver> = async{
                        let! client = GetReceiverClient()
                        return! client.GetReceiver(receiverToken)
                    }
                    member this.CreateReceiver(receiverConfigurution:ReceiverConfiguration):Async<Receiver> = async{
                        let! client = GetReceiverClient()
                        return! client.CreateReceiver(receiverConfigurution)
                    }
                    member this.DeleteReceiver(receiverToken:string):Async<unit> = async{
                        let! client = GetReceiverClient()
                        return! client.DeleteReceiver(receiverToken)
                    }
                    member this.ConfigureReceiver(receiverToken:string, receiverConfiguration:ReceiverConfiguration):Async<unit> = async{
                        let! client = GetReceiverClient()
                        return! client.ConfigureReceiver(receiverToken, receiverConfiguration)
                    }
                    member this.SetReceiverMode(receiverToken:string, receiverMode:ReceiverMode):Async<unit> = async{
                        let! client = GetReceiverClient()
                        return! client.SetReceiverMode(receiverToken, receiverMode)
                    }
                    member this.GetReceiverState(receiverToken:string):Async<ReceiverStateInformation> = async{
                        let! client = GetReceiverClient()
                        return! client.GetReceiverState(receiverToken)
                    }
                end
                interface IRecordingAsync with
                    member this.GetServiceCapabilities():Async<Capabilities7> = async{
                        let! client = GetRecordingsClient()
                        return! client.GetServiceCapabilities()
                    }
                    member this.CreateRecording(recordingConfig:RecordingConfiguration) = async{
                        let! client = GetRecordingsClient()
                        return! client.CreateRecording(recordingConfig)
                    }
                    member this.DeleteRecording(recordingToken:string):Async<unit> = async{
                        let! client = GetRecordingsClient()
                        return! client.DeleteRecording(recordingToken)
                    }
                    member this.GetRecordings():Async<GetRecordingsResponseItem[]> = async{
                        let! client = GetRecordingsClient()
                        return! client.GetRecordings()
                    }
                    member this.SetRecordingConfiguration(recordingToken:string, recordingConfig:RecordingConfiguration):Async<unit> = async{
                        let! client = GetRecordingsClient()
                        return! client.SetRecordingConfiguration(recordingToken, recordingConfig)
                    }
                    member this.GetRecordingConfiguration(recordingToken:string):Async<RecordingConfiguration> = async{
                        let! client = GetRecordingsClient()
                        return! client.GetRecordingConfiguration(recordingToken)
                    }
                    member this.CreateTrack(recordingToken:string, trackConfig:TrackConfiguration):Async<string> = async{
                        let! client = GetRecordingsClient()
                        return! client.CreateTrack(recordingToken,  trackConfig)
                    }
                    member this.DeleteTrack(recordingToken:string, trackToken:string):Async<unit> = async{
                        let! client = GetRecordingsClient()
                        return! client.DeleteTrack(recordingToken, trackToken)
                    }
                    member this.GetTrackConfiguration(recordingToken:string, trackToken:string):Async<TrackConfiguration> = async{
                        let! client = GetRecordingsClient()
                        return! client.GetTrackConfiguration(recordingToken, trackToken)
                    }
                    member this.SetTrackConfiguration(recordingToken:string, trackToken:string, trackConfig:TrackConfiguration):Async<unit> = async{
                        let! client = GetRecordingsClient()
                        return! client.SetTrackConfiguration(recordingToken, trackToken, trackConfig)
                    }
                    member this.CreateRecordingJob(jobConfiguration:RecordingJobConfiguration):Async<string* RecordingJobConfiguration> = async{
                        let! client = GetRecordingsClient()
                        return! client.CreateRecordingJob(jobConfiguration)
                    }
                    member this.DeleteRecordingJob(jobToken:string):Async<unit> = async{
                        let! client = GetRecordingsClient()
                        return! client.DeleteRecordingJob(jobToken)
                    }
                    member this.GetRecordingJobs():Async<GetRecordingJobsResponseItem[]> = async{
                        let! client = GetRecordingsClient()
                        return! client.GetRecordingJobs()
                    }
                    member this.SetRecordingJobConfiguration(jobToken:string, recordingJobConfig:RecordingJobConfiguration):Async<RecordingJobConfiguration> = async{
                        let! client = GetRecordingsClient()
                        return! client.SetRecordingJobConfiguration(jobToken, recordingJobConfig)
                    }
                    member this.GetRecordingJobConfiguration(jobToken:string):Async<RecordingJobConfiguration> = async{
                        let! client = GetRecordingsClient()
                        return! client.GetRecordingJobConfiguration(jobToken)
                    }
                    member this.SetRecordingJobMode(jobToken:string, mode:string):Async<unit> = async{
                        let! client = GetRecordingsClient()
                        return! client.SetRecordingJobMode(jobToken, mode)
                    }
                    member this.GetRecordingJobState(jobToken:string):Async<RecordingJobStateInformation> = async{
                        let! client = GetRecordingsClient()
                        return! client.GetRecordingJobState(jobToken)
                    }
                end
                interface IAnalyticsDeviceAsync with
                    member this.GetServiceCapabilities():Async<Capabilities12> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.GetServiceCapabilities()
                    }
                    member this.DeleteAnalyticsEngineControl(configurationToken:string):Async<unit> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.DeleteAnalyticsEngineControl(configurationToken)
                    }
                    member this.CreateAnalyticsEngineControl(configuration:AnalyticsEngineControl):Async<AnalyticsEngineInput[]> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.CreateAnalyticsEngineControl(configuration)
                    }
                    member this.SetAnalyticsEngineControl(configuration:AnalyticsEngineControl, forcePersistence:bool):Async<unit> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.SetAnalyticsEngineControl(configuration, forcePersistence)
                    }
                    member this.GetAnalyticsEngineControl(configurationToken:string):Async<AnalyticsEngineControl> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.GetAnalyticsEngineControl(configurationToken)
                    }
                    member this.GetAnalyticsEngineControls():Async<AnalyticsEngineControl[]> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.GetAnalyticsEngineControls()
                    }
                    member this.GetAnalyticsEngine(configurationToken:string):Async<AnalyticsEngine> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.GetAnalyticsEngine(configurationToken)
                    }
                    member this.GetAnalyticsEngines():Async<AnalyticsEngine[]> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.GetAnalyticsEngines()
                    }
                    member this.SetVideoAnalyticsConfiguration(configuration:VideoAnalyticsConfiguration, forcePersistence:bool):Async<unit> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.SetVideoAnalyticsConfiguration(configuration, forcePersistence)
                    }
                    member this.SetAnalyticsEngineInput(configuration:AnalyticsEngineInput, forcePersistence:bool):Async<unit> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.SetAnalyticsEngineInput(configuration, forcePersistence)
                    }
                    member this.GetAnalyticsEngineInput(configurationToken:string):Async<AnalyticsEngineInput> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.GetAnalyticsEngineInput(configurationToken)
                    }
                    member this.GetAnalyticsEngineInputs():Async<AnalyticsEngineInput[]> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.GetAnalyticsEngineInputs()
                    }
                    member this.GetAnalyticsDeviceStreamUri(streamSetup:StreamSetup, analyticsEngineControlToken:string):Async<string> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.GetAnalyticsDeviceStreamUri(streamSetup, analyticsEngineControlToken)
                    }
                    member this.GetVideoAnalyticsConfiguration(configurationToken:string):Async<VideoAnalyticsConfiguration> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.GetVideoAnalyticsConfiguration(configurationToken)
                    }
                    member this.CreateAnalyticsEngineInputs(configuration:AnalyticsEngineInput[], forcePersistence:bool[]):Async<AnalyticsEngineInput[]> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.CreateAnalyticsEngineInputs(configuration, forcePersistence)
                    }
                    member this.DeleteAnalyticsEngineInputs(configurationToken:string[]):Async<unit> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.DeleteAnalyticsEngineInputs(configurationToken)
                    }
                    member this.GetAnalyticsState(analyticsEngineControlToken:string):Async<AnalyticsStateInformation> = async{
                        let! client = GetAnalyticsDeviceClient()
                        return! client.GetAnalyticsState(analyticsEngineControlToken)
                    }
                end
                interface IActionEngineAsync with
                    member this.GetSupportedActions():Async<SupportedActions> = async{
                        let! client = GetActionEngineClient()
                        return! client.GetSupportedActions()
                    }
                    member this.GetActions(): Async<Action1[]> = async {
                        let! client = GetActionEngineClient()
                        return! client.GetActions()
                    }
                    member this.CreateActions(actions:ActionConfiguration[]): Async<Action1[]> = async{
                        let! client = GetActionEngineClient()
                        return! client.CreateActions(actions)
                    }
                    member this.DeleteActions(tokens:string[]): Async<unit> = async{
                        let! client = GetActionEngineClient()
                        return! client.DeleteActions(tokens)
                    }
                    member this.ModifyActions(actions:Action1[]): Async<unit> = async {
                        let! client = GetActionEngineClient()
                        return! client.ModifyActions(actions)
                    }
                    member this.GetServiceCapabilities():Async<ActionEngineCapabilities> = async{
                        let! client = GetActionEngineClient()
                        if IsNull client then
                            return null
                        else
                            return! client.GetServiceCapabilities() 
                    }
                    
                    member this.GetActionTriggers():Async<ActionTrigger[]> = async{
                        let! client = GetActionEngineClient()
                        return! client.GetActionTriggers()
                    }
                    member this.CreateActionTriggers(actionTriggers:ActionTriggerConfiguration[]):Async<ActionTrigger[]> = async{
                        let! client = GetActionEngineClient()
                        return! client.CreateActionTriggers(actionTriggers)
                    }
                    member this.DeleteActionTriggers(tokens:string[]):Async<unit> = async{
                        let! client = GetActionEngineClient()
                        return! client.DeleteActionTriggers(tokens)
                    }
                    member this.ModifyActionTriggers(actionTriggers:ActionTrigger[]):Async<unit> = async{
                        let! client = GetActionEngineClient()
                        return! client.ModifyActionTriggers(actionTriggers)
                    }
                end
                interface IReplayAsync with
                    member this.GetServiceCapabilities():Async<Capabilities10> = async{
                        let! rep = GetReplayClient()
                        return! rep.GetServiceCapabilities()
                    }
                    member this.GetReplayUri(recordingToken:string, streamSetup:StreamSetup):Async<string> = async{
                        let! rep = GetReplayClient()
                        return! rep.GetReplayUri(recordingToken, streamSetup)
                    }
                    member this.GetReplayConfiguration():Async<ReplayConfiguration> = async{
                        let! rep = GetReplayClient()
                        return! rep.GetReplayConfiguration()
                    }
                    member this.SetReplayConfiguration(replayConfiguration:ReplayConfiguration):Async<unit> = async{
                        let! rep = GetReplayClient()
                        return! rep.SetReplayConfiguration(replayConfiguration)
                    }
                end
            }
            
        
    end




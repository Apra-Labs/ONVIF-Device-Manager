namespace odm.core//.onvif
    open System
    open System.Collections.Generic
    open System.Linq
    open System.Net
    open System.Net.NetworkInformation
    open System.Net.Sockets
    open System.ServiceModel
    open System.ServiceModel.Channels
    open System.ServiceModel.Description
    open System.ServiceModel.Discovery
    open System.ServiceModel.Dispatcher
    open System.Text
    open System.Threading
    open System.Reactive.Concurrency
    open System.Reactive.Disposables
    open System.Reactive.Linq
    open System.Reactive.Subjects
    open System.Xml

    open onvif.services
    open onvif
    open utils
    open utils.fsharp

    type ObserverState = 
        | Subscribed
        | Completed
        | Disposed

    type WsDiscoveredEndpoint(metadata:EndpointDiscoveryMetadata, messageSequence:DiscoveryMessageSequence) = class
        member this.metadata = metadata
        member this.messageSequence = messageSequence
    end
    type WsDiscoveryObservable(factory:Func<DiscoveryClient>) = class
        do if factory |> IsNull then raise <| new ArgumentNullException("factory")
        new (discoveryEndpoint:DiscoveryEndpoint) = 
            if discoveryEndpoint |> IsNull then raise <| new ArgumentNullException("discoveryEndpoint")
            WsDiscoveryObservable(fun()->new DiscoveryClient(discoveryEndpoint))
        
        member this.Find(findCriteria:FindCriteria):IObservable<WsDiscoveredEndpoint> =
            Observable.Create(fun (observer:IObserver<WsDiscoveredEndpoint>) ->
                let dc = factory.Invoke()
                let completed = ref false
                let sync = new Object()
                let dic = new HashSet<string>();
                let disp = new CompositeDisposable()
                let complete() = 
                    completed := true
                    dic.Clear()
                    dc.Close()
                    disp.Dispose()

                dc.FindCompleted.Subscribe(fun (e:FindCompletedEventArgs) -> 
                    lock dic (fun()->
                        if not !completed then
                            complete()
                            if e.Error |> NotNull then
                                try
                                    observer.OnError(e.Error) 
                                with err-> 
                                    //swallow error
                                    dbg.Error(err)
                            else
                                try
                                    observer.OnCompleted() 
                                with err-> 
                                    //swallow error
                                    dbg.Error(err)
                    )
                ) |> disp.Add

                dc.FindProgressChanged.Subscribe(fun (e:FindProgressChangedEventArgs) ->
                    lock dic (fun()->
                        if not !completed then
                            if (dic.Add(e.EndpointDiscoveryMetadata.Address.Uri.OriginalString)) then
                                let ep = new WsDiscoveredEndpoint(e.EndpointDiscoveryMetadata, e.MessageSequence)
                                try 
                                    observer.OnNext(ep) 
                                with err->
                                    //swallow error 
                                    dbg.Error(err)
                    )
                ) |> disp.Add
                using (new OperationContextScope(dc.InnerChannel)) (fun _ ->
                    OperationContext.Current.OutgoingMessageHeaders.MessageId <- new UniqueId(sprintf "uuid:%s" (Guid.NewGuid().ToString()))
                    dc.FindAsync(findCriteria, sync)
                )
                
                Disposable.Create(fun () ->
                    lock dic (fun () ->
                        if not !completed then
                            dc.CancelAsync(sync)
                            complete()
                    )
                )
            )
        
        
        member d.Resolve(resolveCriteria: ResolveCriteria): IObservable<EndpointDiscoveryMetadata> = 
            Observable.Create(fun (observer:IObserver<EndpointDiscoveryMetadata>) ->
                let dc = factory.Invoke()
                let completed = ref false
                let sync = new Object()
                let dic = new HashSet<string>();
                let disp = new CompositeDisposable()
                let complete() = 
                    completed := true
                    dic.Clear()
                    dc.Close()
                    disp.Dispose()
                dc.ResolveCompleted.Subscribe(fun (e:ResolveCompletedEventArgs) ->
                    lock dic (fun()->
                        if not !completed then
                            complete()
                            if e.Error |> NotNull then
                                try 
                                    observer.OnError(e.Error) 
                                with err->
                                    //swallow error 
                                    dbg.Error(err)
                            else
                                if NotNull(e.Result) && NotNull(e.Result.EndpointDiscoveryMetadata) then
                                    try 
                                        observer.OnNext(e.Result.EndpointDiscoveryMetadata) 
                                    with err->
                                        //swallow error 
                                        dbg.Error(err)
                                    try 
                                        observer.OnCompleted() 
                                    with err->
                                        //swallow error 
                                        dbg.Error(err)
                                else
                                    try 
                                        observer.OnError(new TimeoutException()) 
                                    with err->
                                        //swallow error 
                                        dbg.Error(err)
                    )
                ) |> disp.Add
                
                dc.ResolveAsync(resolveCriteria, sync)
                
                Disposable.Create(fun () ->
                    lock dic (fun () ->
                        if not !completed then
                            dc.CancelAsync(sync)
                            complete()
                    )
                )
            )
    end

    type NvtIdentity(endpointReference:String, uris:Uri[], scopes:Uri[]) = class
        member d.uris = uris
        member d.scopes = scopes
        member d.endpointReference = endpointReference
    end

    type INvtNode = interface
        abstract identity: NvtIdentity with get
        abstract RegisterRemovalHandler: Action -> IDisposable
    end
    
    type INvtManager = interface
        abstract ListenHelloBye: unit->IDisposable
        abstract RegisterRemovalHandler: (INvtNode->unit)->IDisposable
        abstract Discover: duration:TimeSpan->IDisposable
        abstract Discover: ip:IPAddress*duration:TimeSpan->IDisposable
        //abstract Discover: uri:Uri*duration:TimeSpan->unit
        //abstract Enumerate: unit->IEnumerable<INvtNode>
        abstract Observe: unit->IObservable<INvtNode>
    end

    type internal NvtOnlineDescriptor = class
        new(identity: NvtIdentity, msgSeq: DiscoveryMessageSequence) = {
            msgSeq = msgSeq
            identity = identity
            removed = false
            removal_handlers = new LinkedList<unit->unit>()
        }
        val identity: NvtIdentity
        val mutable msgSeq: DiscoveryMessageSequence
        val mutable removed: bool
        val removal_handlers: LinkedList<unit->unit>

        member this.SetRemoved() = 
            lock this.removal_handlers (fun ()->
                let removing = not (this.removed)
                this.removed <- true
                (fun ()-> 
                    if removing then 
                        for h in this.removal_handlers do 
                            try 
                                h() 
                            with err -> 
                                //swallow error 
                                dbg.Error(err)
                )
            ) ()
        

        interface INvtNode with
            member this.identity = 
                this.identity

            member this.RegisterRemovalHandler (handler:Action) :IDisposable = 
                lock this.removal_handlers (fun ()->
                    if this.removed then
                        (fun ()-> 
                            handler.Invoke()
                            Disposable.Empty
                        )
                    else
                        let flag = new BooleanDisposable()
                        let node = this.removal_handlers.AddLast(fun ()->
                            // ensure that handler already has been called or never will be
                            lock flag (fun ()->
                                if not (flag.IsDisposed) then 
                                    try 
                                        handler.Invoke() 
                                    with err-> 
                                        //swallow error 
                                        dbg.Error(err)
                            )
                        )
                        (fun()->Disposable.Create(fun ()->
                            lock this.removal_handlers (fun ()->
                                if not(this.removed) then this.removal_handlers.Remove(node)
                            )
                            // ensure that handler already has been called or never will be
                            lock flag (fun ()->flag.Dispose())
                        ))
                )() 
        end
    end

    type internal NvtOfflineDescriptor = class
        new(msgSeq: DiscoveryMessageSequence) = {
            msgSeq = msgSeq
        }
        val mutable msgSeq: DiscoveryMessageSequence
    end
        
    /// Some cameras never answer the multicast probe, only one sent straight to them.
    /// So discovery also sends the probe to every address of each local IPv4 subnet
    /// (subnets of up to 1024 addresses; link-local ones are skipped) and takes the
    /// answers like multicast ones.
    type internal UnicastProbe() = class
        static let sequences = new DiscoveryMessageSequenceGenerator()

        static member ProbeMessage(messageId:Guid) =
            String.Concat(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
                "<e:Envelope xmlns:e=\"http://www.w3.org/2003/05/soap-envelope\" ",
                "xmlns:w=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" ",
                "xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\" ",
                "xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\">",
                "<e:Header><w:MessageID>uuid:", messageId.ToString(), "</w:MessageID>",
                "<w:To e:mustUnderstand=\"true\">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>",
                "<w:Action e:mustUnderstand=\"true\">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>",
                "</e:Header><e:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></e:Body></e:Envelope>"
            )

        /// each local IPv4 address, with the other addresses of its subnet
        static member Subnets():(System.Net.IPAddress * System.Net.IPAddress[]) list =
            let toUInt (b:byte[]) = (uint32 b.[0] <<< 24) ||| (uint32 b.[1] <<< 16) ||| (uint32 b.[2] <<< 8) ||| uint32 b.[3]
            let toAddress (v:uint32) = new System.Net.IPAddress([| byte (v >>> 24); byte (v >>> 16); byte (v >>> 8); byte v |])
            [ for ni in NetworkInterface.GetAllNetworkInterfaces() do
                if ni.OperationalStatus = OperationalStatus.Up &&
                   ni.NetworkInterfaceType <> NetworkInterfaceType.Loopback &&
                   ni.NetworkInterfaceType <> NetworkInterfaceType.Tunnel then
                    for ua in ni.GetIPProperties().UnicastAddresses do
                        if ua.Address.AddressFamily = AddressFamily.InterNetwork && NotNull(ua.IPv4Mask) then
                            let ip = toUInt(ua.Address.GetAddressBytes())
                            let mask = toUInt(ua.IPv4Mask.GetAddressBytes())
                            let count = (~~~mask) + 1u
                            let linkLocal = (ip >>> 16) = 0xA9FEu
                            if not linkLocal && count >= 4u && count <= 1024u then
                                let network = ip &&& mask
                                let hosts = [| for h in network + 1u .. network + count - 2u do if h <> ip then yield toAddress h |]
                                yield (ua.Address, hosts) ]

        /// a ProbeMatch answer as discovery metadata (None if it isn't one)
        static member Parse(buffer:byte[], count:int):(EndpointDiscoveryMetadata * DiscoveryMessageSequence) option =
            try
                let doc = new XmlDocument()
                doc.LoadXml(Encoding.UTF8.GetString(buffer, 0, count))
                let text xpath =
                    match doc.SelectSingleNode(xpath) with
                    | null -> null
                    | node -> node.InnerText
                let address = text "//*[local-name()='ProbeMatch']/*[local-name()='EndpointReference']/*[local-name()='Address']"
                let xaddrs = text "//*[local-name()='ProbeMatch']/*[local-name()='XAddrs']"
                let scopes = text "//*[local-name()='ProbeMatch']/*[local-name()='Scopes']"
                let split (s:string) = s.Split([|' '; '\t'; '\r'; '\n'|], StringSplitOptions.RemoveEmptyEntries)
                if String.IsNullOrWhiteSpace(address) || String.IsNullOrWhiteSpace(xaddrs) then
                    None
                else
                    let meta = new EndpointDiscoveryMetadata()
                    meta.Address <- new EndpointAddress(address.Trim())
                    for x in split xaddrs do
                        let ok, uri = Uri.TryCreate(x, UriKind.Absolute)
                        if ok then meta.ListenUris.Add(uri)
                    if NotNull(scopes) then
                        for x in split scopes do
                            let ok, uri = Uri.TryCreate(x, UriKind.Absolute)
                            if ok then meta.Scopes.Add(uri)
                    if meta.ListenUris.Count = 0 then None else Some(meta, sequences.Next())
            with err ->
                None

        /// probes every subnet address (twice, a moment apart) and hands each answer to found until duration is up
        static member Sweep(duration:TimeSpan, found:EndpointDiscoveryMetadata * DiscoveryMessageSequence -> unit):IDisposable =
            let cancel = new CancellationTokenSource()
            for (local, hosts) in UnicastProbe.Subnets() do
                let socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                try
                    socket.Bind(new IPEndPoint(local, 0))
                    // (no "connection reset" errors from the ICMP answers of addresses with nothing on them)
                    let SIO_UDP_CONNRESET = -1744830452
                    socket.IOControl(SIO_UDP_CONNRESET, [| 0uy; 0uy; 0uy; 0uy |], null) |> ignore
                    let message = Encoding.UTF8.GetBytes(UnicastProbe.ProbeMessage(Guid.NewGuid()))
                    let work() =
                        try
                            try
                                let until = DateTime.UtcNow + duration
                                let buffer = Array.zeroCreate<byte> 65536
                                let mutable sweeps = 0
                                let mutable nextSweep = DateTime.UtcNow
                                while not cancel.IsCancellationRequested && DateTime.UtcNow < until do
                                    if sweeps < 2 && DateTime.UtcNow >= nextSweep then
                                        for host in hosts do
                                            try
                                                socket.SendTo(message, new IPEndPoint(host, 3702)) |> ignore
                                            with err -> ()
                                        sweeps <- sweeps + 1
                                        nextSweep <- DateTime.UtcNow + TimeSpan.FromSeconds(1.0)
                                    if socket.Poll(200000, SelectMode.SelectRead) then
                                        try
                                            let n = socket.Receive(buffer)
                                            match UnicastProbe.Parse(buffer, n) with
                                            | Some answer -> found answer
                                            | None -> ()
                                        with err -> ()
                            with err ->
                                dbg.Error(err)
                        finally
                            socket.Close()
                    let thread = new Thread(work)
                    thread.IsBackground <- true
                    thread.Start()
                with err ->
                    dbg.Error(err)
                    socket.Close()
            Disposable.Create(fun () -> cancel.Cancel())
    end

    type NvtManager() = class
        let m_subj = new Subject<INvtNode>()
        let tramp = new Trampoline()
        let online_nodes = new Dictionary<string, NvtOnlineDescriptor>()
        let offline_nodes = new Dictionary<string, NvtOfflineDescriptor>()

        let notify_offline (node:NvtOnlineDescriptor) = tramp.Drop(fun()->
            node.SetRemoved()
        )

        let notify_online (node:NvtOnlineDescriptor) = tramp.Drop(fun()->
            m_subj.OnNext(node :> INvtNode)
        )

        let process_online (ep_meta:EndpointDiscoveryMetadata, msgSeq:DiscoveryMessageSequence) = 
            let ep_ref = ep_meta.Address.Uri.OriginalString
            let uris = ep_meta.ListenUris.ToArray()
            let scopes = ep_meta.Scopes.ToArray()
            let identity = new NvtIdentity(ep_ref, uris, scopes)
            let node  = new NvtOnlineDescriptor(identity, msgSeq)
            tramp.Drop(fun ()->
                let exist, origin_node = online_nodes.TryGetValue(ep_ref)
                if not exist then
                    let conflicted_nodes = 
                        online_nodes |> Seq.filter (fun kv ->
                            kv.Value.identity.uris.Intersect(uris).Any()
                        ) |> Seq.toArray

                    conflicted_nodes |> Seq.iter (fun kv-> notify_offline kv.Value)
                    conflicted_nodes |> Seq.iter (fun kv->
                        online_nodes.Remove(kv.Key) |> ignore
                    )
                    online_nodes.Add(ep_ref, node)
                    notify_online node
                else
                    let need_to_refresh =
                        not(origin_node.identity.uris.SequenceEqual(uris))

                    if need_to_refresh then
                        notify_offline origin_node
                        online_nodes.[ep_ref] <- node
                        notify_online node
            )

        let process_offline (ep_meta:EndpointDiscoveryMetadata, msgSeq:DiscoveryMessageSequence) = 
            tramp.Drop(fun ()->
                let ep_ref = ep_meta.Address.Uri.OriginalString
                let is_online, node = online_nodes.TryGetValue(ep_ref)
                let need_to_remove = 
                    is_online && //not phantom
                    msgSeq.CanCompareTo(node.msgSeq) && // same sequence context ???
                    msgSeq.CompareTo(node.msgSeq)>=0 // not reordered

                if need_to_remove then 
                    online_nodes.Remove(ep_ref) |> ignore
                    notify_offline node
                    //offline_nodes.Add(ep_ref, new NvtOfflineDescriptor(msgSeq))
            )

        
        let subscribe = 
            let subscriber_cnt = ref 0
            let gate = new obj();
            let announcement_srv = new AnnouncementService()
            //let announcement_ep = new UdpAnnouncementEndpoint(DiscoveryVersion.WSDiscoveryApril2005)

            //hello handler
            announcement_srv.OnlineAnnouncementReceived.AddHandler(fun obj args ->
                let ep_meta = args.EndpointDiscoveryMetadata
                let msg_seq = args.MessageSequence
                //let nvt_xqn = new XmlQualifiedName("NetworkVideoTransmitter", @"http://www.onvif.org/ver10/network/wsdl")
                //if ep_meta.ContractTypeNames.Contains(nvt_xqn) then 
                process_online(ep_meta, msg_seq)
            )
            //bye handler
            announcement_srv.OfflineAnnouncementReceived.AddHandler(fun obj args ->
                let ep_meta = args.EndpointDiscoveryMetadata
                let msg_seq = args.MessageSequence
                process_offline(ep_meta, msg_seq)
            )
            let srv_disp = new SerialDisposable()
            let start_srv() = 
                srv_disp.Disposable <- null
                let host = new ServiceHost(announcement_srv)
//                let d = host.UnknownMessageReceived.Subscribe(fun args -> 
//                    failwith "not implemented"
//                )
                let open_host() = Async.FromBeginEnd(host.BeginOpen, host.EndOpen)
                let close_host() = async{
                    do! Async.SwitchToThreadPool()
                    do! Async.FromBeginEnd(host.BeginClose, host.EndClose)
                }
                let announcementEp = 
                    let ep = new UdpAnnouncementEndpoint(DiscoveryVersion.WSDiscoveryApril2005)
                    //BUGFIX: wcf sends multicasts faults, in case if it didn't uderstand received discovery message
                    match ep.Binding with
                    | :? CustomBinding as binding -> 
                        binding.Elements.Insert(0, new MulticastCapabilitiesBindingElement(true))
                    | _ ->
                        log.WriteError("binding of UdpAnnouncementEndpoint is not of type CustomBinding, fix for multicast udp storm will not be applied")
                        dbg.Break()
                    ep
                host.AddServiceEndpoint(announcementEp)
                //let property = announcement_Ep.Binding.GetProperty<IBindingMulticastCapabilities>(new BindingParameterCollection());
                //var binding = new CustomBinding();
                //binding.Elements.Add(new MulticastCapabilitiesBindingElement(true));
                //binding.Elements.AddRange(announcementEp.Binding.CreateBindingElements().ToArray());
                //host.AddServiceEndpoint(typeof(IAnnouncementContractApril2005), binding, Ipv4MulticastAddress);
                //host.Description.Behaviors.Add(new SrvBeh());

                //host.Open()
                open_host() |> Async.StartImmediate
                srv_disp.Disposable <- Disposable.Create(fun ()->
                   close_host() |> Async.StartImmediate
                )
            
            let stop_srv() = 
                srv_disp.Disposable <- null
            
            //TODO: remote discovery
//            let cts = new CancellationTokenSource()
//            disp.Add(cts)
//            Async.StartImmediate(async{
//                let dp = new DiscoveryLookupAsync(null) :> IDiscoveryLookupAsync
//                //let types = new XmlQualifiedName
//                let scopesType = new ScopesType()
//                scopesType.MatchBy <- findCriteria.ScopeMatchBy.OriginalString
//                scopesType.Text <- findCriteria.Scopes |> Seq.map(fun x->x.OriginalString) |> Seq.toArray
//                let! r = dp.Probe(findCriteria.ContractTypeNames, scopesType)
//                for x in r do
//                    ()
//                    //let ep = new WsDiscoveredEndpoint(x. .EndpointDiscoveryMetadata, x.MessageSequence)
//                    //try observer.OnNext(ep) with err->Assert.FailWithError err
//            }, cts.Token)

            fun (observer:IObserver<INvtNode>) ->
                lock gate (fun()->    
                    subscriber_cnt := !subscriber_cnt+1
                    if !subscriber_cnt = 1 then
                        //TODO: create async variant
                        start_srv()
                )
                let subscription = new SingleAssignmentDisposable()
                tramp.Drop(fun()->
                    for kv in online_nodes do observer.OnNext(kv.Value)
                    subscription.Disposable <- m_subj.Subscribe(observer)
                )
                Disposable.Create(fun () -> 
                    lock gate (fun () ->
                        subscriber_cnt := !subscriber_cnt-1
                        if !subscriber_cnt = 0 then
                            stop_srv()
                    )
                    subscription.Dispose()
                )

        interface INvtManager with
            member o.ListenHelloBye() =   
                failwith "not implemented"

            member o.RegisterRemovalHandler handler = 
                failwith "not implemented"

            member o.Discover(duration:TimeSpan):IDisposable = 
                //let discovery_duration = duration;
                let discoveryEp = 
                    let ep = new UdpDiscoveryEndpoint(DiscoveryVersion.WSDiscoveryApril2005)
                    //BUGFIX: wcf sends multicasts faults, in case if it didn't uderstand received discovery message
                    match ep.Binding with
                    | :? CustomBinding as binding -> 
                        binding.Elements.Insert(0, new MulticastCapabilitiesBindingElement(true))
                    | _ ->
                        log.WriteError("binding of UdpAnnouncementEndpoint is not of type CustomBinding, fix for multicast udp storm will not be applied")
                        dbg.Break()
                    ep

                //ep.MaxResponseDelay <- duration
                //ep.TransportSettings.TimeToLive <- 64 //HACK: workaround for VPN
                let disc = new WsDiscoveryObservable(discoveryEp)
                let probe t =
                    let fc = new FindCriteria()
                    fc.ContractTypeNames.Add(t)
                    fc.Duration <- TimeSpan.MaxValue
                    fc.MaxResults <- Int32.MaxValue
                    disc.Find(fc).Timeout(duration, Observable.Empty<WsDiscoveredEndpoint>()).Subscribe(
                        (fun ep->process_online(ep.metadata, ep.messageSequence)),
                        (fun err->dbg.Error(err)),
                        (fun ()->())
                    )
                let disp = [|
                    yield probe(new XmlQualifiedName("NetworkVideoTransmitter", @"http://www.onvif.org/ver10/network/wsdl"))
                    yield probe(new XmlQualifiedName("Device", @"http://www.onvif.org/ver10/device/wsdl"))
                    yield UnicastProbe.Sweep(duration, process_online)
                |]
                upcast new CompositeDisposable(disp)
                
            member o.Discover(ip:IPAddress, duration:TimeSpan):IDisposable = 
                failwith "not implemented"

//            member o.Discover(uri:Uri, duration:TimeSpan):unit = 
//                failwith "not implemented"
            
//            member o.Enumerate():IEnumerable<INvtNode> = 
//                failwith "not implemented"

            member o.Observe():IObservable<INvtNode> = 
                Observable.Create<INvtNode>(fun (observer:IObserver<INvtNode>) ->
                    subscribe observer
                )
                

        end
        member this.Subscribe(observer:IObserver<(NvtIdentity->IDisposable)->IDisposable>) = 
            let observable = this :> INvtManager
            let transform(node:INvtNode):(NvtIdentity->IDisposable)->IDisposable =
                let remove_disp = new SingleAssignmentDisposable()
                let reg_disp = node.RegisterRemovalHandler(fun()->
                    remove_disp.Dispose()
                )
                (fun f->
                    remove_disp.Disposable <- 
                        try
                            f(node.identity)
                        with err->
                            reg_disp.Dispose()
                            null

                    Disposable.Create(fun()->
                        reg_disp.Dispose()
                        remove_disp.Dispose()
                    )
                )
            (observable.Observe() |> Observable.map(transform)).Subscribe(observer)

        //member this.Subscribe(observer:IObserver<Func<Func<NvtIdentity, IDisposable>, IDisposable>) = 
            
    end
namespace StackOverflow

module WebApiApp =

    open System
    open System.Text
    open System.Net
    open Suave
    open Suave.Filters
    open Suave.Logging
    open Suave.Operators
    open Suave.RequestErrors
    open Suave.Successful
    open Suave.Swagger.FunnyDsl
    open Suave.Swagger.Swagger

    let [<Literal>] MaxResultCount = 100

    /// Creates a web part from a function (to enable lazy computation)
    let delay (f:unit -> WebPart) ctx = 
      async { return! f () ctx }

    let mapUrlSegmentToEventType segment =
        match segment with
        | "asa" -> PersistentTypes.AkamaiStorageAssignmentPrefix
        | "osa" -> PersistentTypes.OriginServiceAssignmentPrefix
        | "oaa" -> PersistentTypes.OriginAssetAssignmentPrefix
        | "fd" -> PersistentTypes.FileDistributionPrefix
        | "fg" -> PersistentTypes.FileGroupPrefix
        | _ -> segment

    let parseQueryParam q f d =
        match q with 
        | Choice1Of2 x -> f x
        | Choice2Of2 _ -> d
    
    let getResponseWithFilter f = fun (ctx : HttpContext) ->
        async {
            let skipCount = parseQueryParam (ctx.request.queryParam "skip") <| Int32.Parse <| 0
            let takeCount = parseQueryParam (ctx.request.queryParam "top") <| Int32.Parse <| MaxResultCount
            let! result = f skipCount takeCount
            let result =
                result  
                |> Seq.toList
                |> Serializer.serialize 
            return! OK result ctx >>= Writers.setMimeType "application/json; charset=utf-8"
        }

    let getIpAddressStatus (atvConfig : AppearTVYaml) ipAddress =
        Http.GetAsString 
            <| Some { Username = atvConfig.AppearTV.GeoblockCheck.Username; Password = atvConfig.AppearTV.GeoblockCheck.Password } 
            <| sprintf "%s%s/verify_cdn/?format=json" atvConfig.AppearTV.GeoblockCheck.URL.AbsoluteUri ipAddress
        |> function 
            | Ok text -> text
            | Result.Error (errorCode, text) -> sprintf "HTTP errors %d retrieving IP geoblock information (%s)" errorCode text 
        |> OK
        >=> Writers.setHeaderValue "Access-Control-Allow-Origin" "*"  
         

    [<NoEquality;NoComparison>]
    type PersistenceState = {
        PersistenceId : string
        State : obj
    }
        
    let resetEvents clientId eventId =
        deleteJournalEvents clientId eventId
        OK |> Serializer.toJsonSync
        
    let spawnHealthChecker (system : ActorSystem) oddjobConfig akamaiConfig : IActorRef<HealthCheck.Actors.QueryState> =
        let componentsSpec = HealthCheck.createRootComponentSpecification system oddjobConfig akamaiConfig
        let actorNameBase = "Health"
        let healthCheckerManager =
            system.ActorOf(
                ClusterSingletonManager.Props(
                    (props (HealthCheck.Actors.rootActor componentsSpec)).ToProps(), 
                    ClusterSingletonManagerSettings.Create(system).WithRole("WebApi")),
                makeActorName [actorNameBase])
        typed <| system.ActorOf(
                ClusterSingletonProxy.Props(
                    healthCheckerManager.Path.ToStringWithoutAddress(),
                    ClusterSingletonProxySettings.Create(system).WithRole("WebApi")),
                makeActorName [actorNameBase; "proxy"])
    
    let private app (system:ActorSystem) (oddjobConfig : OddjobYaml) atvConfig akamaiConfig =
        let persistenceQuery = PersistenceUtils.createPersistenceQuery system
       
        let reader = 
            if oddjobConfig.Features.WebApiRetrieveProgramUsingSharding
            then EventJournalUtils.Sharding.createStateReader system oddjobConfig
            else EventJournalUtils.Routing.createStateReader system persistenceQuery
        let healthChecker = spawnHealthChecker system oddjobConfig akamaiConfig

        let api = 
            swagger {
            
                // swagger, really not neccessary, but suave.swagger seems to mess up the first route in list
                for route in getOf (path "/" >=> Redirection.redirect "/") do
                    yield description Of route is "Swagger documentation"
                    yield urlTemplate Of route is "/"
                    yield route
                            |> tag "swagger"
            
            
                // health 
                for route in getOf (path "/health" >=>  warbler (fun _ ->
                                    let (status : HealthStatus) = healthChecker <? HealthCheck.Actors.QueryState |> Async.RunSynchronously
                                    status |> Serializer.toJsonSync)) do
                    yield description Of route is "Health check for the Oddjob system"
                    yield urlTemplate Of route is "/health"
                    yield route 
                            |> addResponse 200 "returns health status" None // actual return type causes infinite loop in suave.swagger
                            |> tag "health"
                                        
            
                // ip 
                for route in getOf (pathScan "/ip/%s" (fun (ipAddress) -> getIpAddressStatus atvConfig ipAddress)) do
                    yield description Of route is "Used to check geoblock information for given ip address"
                    yield urlTemplate Of route is "/ip/{ipAddress}"
                    yield parameter "ipAddress" Of route 
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield route 
                            |> addResponse 200 "Returns informationon of geoblock of given ip address" (Some typeof<string>)
                            |> tag "ip"
                
                
                // status endpoints
                for route in getOf (pathScan "/status/%s/%s" (fun (clientId,eventId) ->
                        GroupUtils.getAggregatedSummary system.Log reader oddjobConfig atvConfig akamaiConfig clientId eventId |> Serializer.toJsonFromAsync)) do
                    yield description Of route is "Shows aggregated summary of the distribution status of a given program"
                    yield urlTemplate Of route is "/status/{clientId}/{eventId}"
                    yield parameter "clientId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path})
                    yield parameter "eventId" Of route 
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield route 
                        |> addResponse 200 "returns group summary of the event" (Some typeof<GroupSummary>)
                        |> tag "status"
                        
                
                // event endpoints   
                for route in getOf (pathScan "/events/%s/%s/state" (fun (segment, eventId) -> 
                                                 let eventType = mapUrlSegmentToEventType segment 
                                                 getJournalState reader eventType eventId |> Serializer.toJsonFromAsync)) do
                    yield description Of route is
                        "Shows state of event journal. 
                        Segment is one of asa (Akamai service assignment), osa (Origin service assignement), oaa (origin asset assignment), fd (file distribution) or fg (file group).
                        EventId is for instance of the form \"ps/obui18001308~obui18001308aa/obui18001308aa_270.mp4\" for fd."
                    yield urlTemplate Of route is "/events/{segment}/{eventId}/state"
                    yield parameter "segment" Of route  
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield parameter "eventId" Of route 
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield route 
                            |> addResponse 200 "Returns journal state" (Some typeof<PersistenceState>)
                            |> tag "events"
                                        
                                             
                for route in getOf (pathScan "/events/%s/%s" (fun (segment, eventId) -> 
                                                 let eventType = mapUrlSegmentToEventType segment
                                                 getResponseWithFilter (EventJournalUtils.Queries.getJournalEventsById persistenceQuery eventType (eventId.Replace("/", "~"))))) do
                    yield description Of route is 
                        "Shows journal events
                         Segment is one of asa (Akamai service assignment), osa (Origin service assignement), oaa (origin asset assignment), fd (file distribution) or fg (file group).
                         EventId is for instance of the form \"ps/obui18001308~obui18001308aa/obui18001308aa_270.mp4\" for fd."
                    yield urlTemplate Of route is "/events/{segment}/{eventId}"
                    yield parameter "segment" Of route  
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield parameter "eventId" Of route 
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield parameter "top" Of route 
                            (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                    yield parameter "skip" Of route 
                            (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                    yield route 
                            |> addResponse 200 "Returns list of journal events" (Some typeof<seq<JournalEvent>>)
                            |> tag "events"
                            
                for route in deleteOf (pathScan "/events/reset/%s/%s" (fun (clientId,eventId) ->
                                                           resetEvents clientId eventId)) do
                    yield description Of route is 
                        "Deletes event from event journals and snapshots
                         Segment is one of asa (Akamai service assignment), osa (Origin service assignement), oaa (origin asset assignment), fd (file distribution) or fg (file group).
                         EventId is for instance of the form \"ps/obui18001308~obui18001308aa/obui18001308aa_270.mp4\" for fd."
                    yield urlTemplate Of route is "/events/reset/{segment}/{eventId}"
                    yield parameter "segment" Of route  
                                (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield parameter "eventId" Of route 
                                (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield route 
                            |> addResponse 200 "" None 
                            |> tag "events"
                            
                            
               // akamai endpoints            
                for route in getOf (pathScan "/akamai/local/%s" (fun eventId -> 
                    eventId |> Akamai.getLocalInfo reader |> Serializer.toJsonFromAsync)) do
                    
                    yield description Of route is "Gives information on local Akamai"
                    yield urlTemplate Of route is "/akamai/local/{persistenceId}"
                    yield parameter "persistenceId" Of route  
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield route 
                            |> addResponse 200 "Returns local Akamai information" (Some typeof<LocalAkamai>)
                            |> tag "akamai"
                                
                for route in getOf (pathScan "/akamai/remote/%s" (fun qualifiedPersistenceId ->
                    let piProgId =
                        qualifiedPersistenceId.Split ([|'/'; '~'|])
                        |> Seq.skip 1
                        |> Seq.head
                        |> PiProgId.create 
                    Akamai.getRemoteInfo reader akamaiConfig qualifiedPersistenceId piProgId |> Serializer.toJsonFromAsync)) do
                    
                    yield description Of route is "Gives information on remote Akamai"
                    yield urlTemplate Of route is "/akamai/remote/{persistenceId}"
                    yield parameter "persistenceId" Of route  
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield route 
                            |> addResponse 200 "Returns remote Akamai information" (Some typeof<RemoteAkamai>)
                            |> tag "akamai"
                                
                for route in getOf (pathScan "/akamai/%s" (fun qualifiedPersistenceId ->
                    let piProgId =
                        qualifiedPersistenceId.Split ([|'/'; '~'|])
                        |> Seq.skip 1
                        |> Seq.head
                        |> PiProgId.create
                    Akamai.getGroupInfo reader akamaiConfig qualifiedPersistenceId piProgId |> Serializer.toJsonFromAsync)) do
                    
                    yield description Of route is "Gives information about Akamai"
                    yield urlTemplate Of route is "/akamai/{persistenceId}"
                    yield parameter "persistenceId" Of route  
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield route 
                            |> addResponse 200 "Returns both local and remote Akamai information" (Some typeof<AkamaiInfo>)
                            |> tag "akamai"
                
                
                // origin endpoints                
                for route in getOf (pathScan "/origin/local/%s" (fun eventId ->
                                    async {
                                        let! parts = PartUtils.getPartsInfo reader eventId
                                        return! eventId |> Origin.getLocalInfo reader parts
                                    } 
                                    |> Serializer.toJsonFromAsync)) do
                    yield description Of route is "Gives information on local Origin"
                    yield urlTemplate Of route is "/origin/local/{persistenceId}"
                    yield parameter "persistenceId" Of route  
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield route 
                            |> addResponse 200 "Returns local Origin information" (Some typeof<LocalOrigin>)
                            |> tag "origin"
                                
                for route in getOf (pathScan "/origin/remote/%s" (fun eventId -> 
                                    eventId |> Origin.getRemoteInfo reader atvConfig |> Serializer.toJsonFromAsync)) do
                    yield description Of route is "Gives information on remote Origin"
                    yield urlTemplate Of route is "/origin/remote/{persistenceId}"
                    yield parameter "persistenceId" Of route  
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield route 
                            |> addResponse 200 "Returns remote Akamai information" (Some typeof<RemoteOrigin>)
                            |> tag "origin"
                                
                for route in getOf (pathScan "/origin/%s" (fun eventId ->
                                    async {
                                        let! parts = PartUtils.getPartsInfo reader eventId
                                        return! eventId |> Origin.getGroupInfo reader atvConfig parts
                                    } 
                                    |> Serializer.toJsonFromAsync)) do
                    yield description Of route is "Gives information about Origin"
                    yield urlTemplate Of route is "/origin/{persistenceId}"
                    yield parameter "persistenceId" Of route  
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield route 
                            |> addResponse 200 "Returns both local and remote Origin information" (Some typeof<OriginInfo>)
                            |> tag "origin"
              
                
                //ps endpoints
                for route in getOf (pathScan "/ps/rights/%s" (fun eventId -> 
                                    eventId |> PiProgId.create |> PsUtils.getRightsInfo  |> Serializer.toJsonSync)) do
                    yield description Of route is "Shows information about rights for program"
                    yield urlTemplate Of route is "/ps/rights/{piProgId}"
                    yield parameter "piProgId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })                
                    yield route 
                        |> addResponse 200 "Returns rights info" (Some typeof<ReadRightsInfo>)
                        |> tag "ps"
                    
                                
                for route in getOf (pathScan "/ps/files/%s" (fun eventId -> 
                                    eventId |> PiProgId.create |> PsUtils.getFilesInfo  |> Serializer.toJsonSync)) do
                    yield description Of route is "Shows information about program files"
                    yield urlTemplate Of route is "/ps/files/{piProgId}"
                    yield parameter "piProgId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })                
                    yield route 
                        |> addResponse 200 "Returns list of files info" (Some typeof<FilesInfo>)
                        |> tag "ps"
                    
                                
                for route in getOf (pathScan "/ps/transcoding/%s" (fun eventId -> 
                                    eventId |> PiProgId.create |> PsUtils.getTranscodingInfo |> Serializer.toJsonSync)) do
                    yield description Of route is "Show information about transcoding of program"
                    yield urlTemplate Of route is "/ps/transcoding/{piProgId}"
                    yield parameter "piProgId" Of route  
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })                
                    yield route 
                        |> addResponse 200 "Returns list of transcoding info" (Some typeof<TranscodingInfo>)
                        |> tag "ps"
                                
                for route in getOf (pathScan "/ps/service/%s" (fun eventId -> 
                                    eventId |> PiProgId.create |> PsUtils.getServiceInfo |> Serializer.toJsonSync)) do
                    yield description Of route is "Gives services information for progam"
                    yield urlTemplate Of route is "/ps/service/{piProgId}"
                    yield parameter "piProgId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })                
                    yield route 
                        |> addResponse 200 "Returns service information" (Some typeof<GranittOriginService>)
                        |> tag "ps"
                                
                for route in getOf (pathScan "/ps/archive/%s" (fun eventId -> 
                                    eventId |> PiProgId.create |> PsUtils.getArchiveFiles oddjobConfig |> Serializer.toJsonSync)) do
                    yield description Of route is "Gives information about the archive files of the program"
                    yield urlTemplate Of route is "/ps/archive/{piProgId}"
                    yield parameter "piProgId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })                
                    yield route 
                        |> addResponse 200 "Returns list of archive parts" (Some typeof<ArchivePart>)
                        |> tag "ps"
                    
                for route in putOf (pathScan "/ps/rights/%s" (fun eventId -> request (fun req ->
                                    let content = Encoding.UTF8.GetString(req.rawForm)
                                    PsUtils.updateRights eventId content |> Serializer.toJsonSync))) do
                    yield description Of route is "Update rights for given program"
                    yield urlTemplate Of route is "/ps/rights/{piProgId}"
                    yield parameter "piProgId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield parameter "rightsInfo" Of route  
                        (fun p -> { p with Type = (Some typeof<WriteRightsInfo>); In=Body })
                    yield route 
                            |> addResponse 200 "Returns list of rights info" (Some typeof<ReadRightsInfo>)
                            |> tag "ps"
                    
                for route in putOf (pathScan "/ps/files/%s" (fun eventId -> request (fun req ->
                                    let content = Encoding.UTF8.GetString(req.rawForm)
                                    PsUtils.restoreFiles eventId content |> Serializer.toJsonSync))) do
                    yield description Of route is "Restore files for given program"
                    yield urlTemplate Of route is "/ps/files/{piProgId}"
                    yield parameter "piProgId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield parameter "filesInfo" Of route  
                        (fun p -> { p with Type = (Some typeof<FilesInfo>); In=Body })
                    yield route 
                            |> addResponse 200 "Returns list of files info" (Some typeof<FilesInfo>)
                            |> tag "ps"
              
                // potion endpoints
                for route in getOf (pathScan "/potion/mapping/%s" (fun eventId -> 
                                  eventId |> PotionUtils.getMappingInfo |> Serializer.toJsonSync)) do 
                    yield description Of route is "Gets potions mapping for given persistence id"
                    yield urlTemplate Of route is "/potion/mapping/{persistenceId}"
                    yield parameter "persistenceId" Of route  
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield route 
                            |> addResponse 200 "Returns potion mapping" (Some typeof<PotionMapping>)
                            |> tag "potion"
                            
                for route in getOf (pathScan "/potion/publication/%s" (fun eventId -> 
                                  eventId |> PotionUtils.getPublicationInfo oddjobConfig |> Serializer.toJsonSync)) do 
                    yield description Of route is "Gets potions publication for given persistence id"
                    yield urlTemplate Of route is "/potion/publication/{persistenceId}"
                    yield parameter "persistenceId" Of route  
                            (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield route 
                            |> addResponse 200 "Returns potion publication" (Some typeof<PotionPublication>)
                            |> tag "potion"
                            
                // queue endpoints
                for route in postOf (pathScan "/queues/ps/programs/%s" (fun programId -> 
                        request (fun req -> 
                            let priority = parseQueryParam (req.queryParam "priority") Byte.Parse 0uy
                            QueueUtils.sendProgamToPsQueue oddjobConfig req ExchangeCategory.PsProgramsWatch programId priority))) do
                    yield description Of route is "Service to force file upload for the given program"
                    yield urlTemplate Of route is "/queues/ps/programs/{piProgId}"
                    yield parameter "piProgId" Of route  
                         (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield parameter "priority" Of route  
                         (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                    yield route 
                            |> addResponse 200 "Url to status end point" (Some typeof<ProgramStatus>)
                            |> tag "queue"
                            
                for route in postOf (path "/queues/ps/programs" >=> request (fun req ->
                                    let content =  Encoding.UTF8.GetString(req.rawForm)
                                    let changeMessage = Serializer.deserialize<PsChangeMessage> content                                
                                    let priority = parseQueryParam (req.queryParam "priority") Byte.Parse 0uy
                                    QueueUtils.sendMessageToPsQueue oddjobConfig req ExchangeCategory.PsProgramsWatch changeMessage priority)) do
                    yield description Of route is "Service to send given change message to programs queue"
                    yield urlTemplate Of route is "/queues/ps/programs"
                    yield parameter "message" Of route  
                        (fun p -> { p with Type = (Some typeof<PsChangeMessage>); In=Body })
                    yield parameter "priority" Of route  
                         (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                    yield route 
                            |> addResponse 200 "Url to status end point" (Some typeof<ProgramStatus>)
                            |> tag "queue"
                            
                for route in postOf (pathScan "/queues/ps/subtitles/%s" (fun programId ->
                        request (fun req -> 
                            let priority = parseQueryParam (req.queryParam "priority") Byte.Parse 0uy
                            QueueUtils.sendProgamToPsQueue oddjobConfig req ExchangeCategory.PsSubtitlesWatch programId priority))) do
                    yield description Of route is "Service to force subtitles update for the given program"
                    yield urlTemplate Of route is "/queues/ps/subtitles/{piProgId}"
                    yield parameter "piProgId" Of route  
                         (fun p -> { p with Type = (Some typeof<string>); In=Path })
                    yield parameter "priority" Of route  
                         (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                    yield route 
                            |> addResponse 200 "Url to status end point" (Some typeof<ProgramStatus>)
                            |> tag "queue"
                            
                for route in postOf (path "/queues/potion" >=> request (fun req ->
                                    let content =  Encoding.UTF8.GetString(req.rawForm)
                                    let message = Serializer.deserialize<PotionCommand> content
                                    let priority = parseQueryParam (req.queryParam "priority") Byte.Parse 0uy
                                    QueueUtils.sendMessageToPotionQueue oddjobConfig message priority)) do
                    yield description Of route is "Service to send messages of types addFile, addSubtitle, changeGeoblock, deleteGroup and deleteSubtitle to Potion queue"
                    yield urlTemplate Of route is "/queues/potion"
                    yield parameter "message" Of route  
                        (fun p -> { p with Type = (Some typeof<Object>); In=Body })
                    yield parameter "priority" Of route  
                         (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                    yield route 
                            |> addResponse 204 "Message successfully sent to queue" None
                            |> tag "queue"
                            
                            
                for route in postOf (path "/queues/ps/subtitles" >=> request (fun req ->
                                    let content =  Encoding.UTF8.GetString(req.rawForm)
                                    let changeMessage = Serializer.deserialize<PsChangeMessage> content                                
                                    let priority = parseQueryParam (req.queryParam "priority") Byte.Parse 0uy
                                    QueueUtils.sendMessageToPsQueue oddjobConfig req ExchangeCategory.PsSubtitlesWatch changeMessage priority)) do
                    yield description Of route is "Service to send given change message to subtitles queue"
                    yield urlTemplate Of route is "/queues/ps/subtitles"
                    yield parameter "message" Of route  
                        (fun p -> { p with Type = (Some typeof<PsChangeMessage>); In=Body })
                    yield parameter "priority" Of route  
                         (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                    yield route 
                            |> addResponse 200 "Url to status end point" (Some typeof<ProgramStatus>)
                            |> tag "queue"
                            
                
                for route in postOf (path "/queues/upload/origin" >=> request (fun req -> 
                                          if not oddjobConfig.Features.WebApiPostToUploadQueue then FORBIDDEN "Request not allowed in this environment"
                                          else
                                            let content = Encoding.UTF8.GetString(req.rawForm)
                                            let message = Serializer.deserialize<UploadMessage> content
                                            let priority = parseQueryParam (req.queryParam "priority") Byte.Parse 0uy
                                            QueueUtils.sendToQueue oddjobConfig ExchangeCategory.OriginUpload (fun () -> NO_CONTENT) message priority)) do
                    yield description Of route is "Service to send given message to origin upload queue"
                    yield urlTemplate Of route is "/queues/upload/origin"
                    yield parameter "message" Of route (fun p -> 
                              { p with Type = (Some typeof<Object>); In = Body })
                    yield parameter "priority" Of route  
                         (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                    yield route
                          |> addResponse 204 "Message successfully sent to queue" None
                          |> addResponse 403 "Forbidden in this environment" None
                          |> tag "queue"
                          
                          
                for route in postOf (path "/queues/upload/akamai" >=> request (fun req -> 
                                          if not oddjobConfig.Features.WebApiPostToUploadQueue then FORBIDDEN "Request not allowed in this environment"
                                          else
                                            let content = Encoding.UTF8.GetString(req.rawForm)
                                            let message = Serializer.deserialize<UploadMessage> content
                                            let priority = parseQueryParam (req.queryParam "priority") Byte.Parse 0uy
                                            QueueUtils.sendToQueue oddjobConfig ExchangeCategory.AkamaiUpload (fun () -> NO_CONTENT) message priority)) do  
                    yield description Of route is "Service to send given message to akamai upload queue"
                    yield urlTemplate Of route is "/queues/upload/akamai"
                    yield parameter "message" Of route (fun p -> 
                              { p with Type = (Some typeof<Object>); In = Body })
                    yield parameter "priority" Of route  
                         (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                    yield route
                          |> addResponse 204 "Message successfully sent to queue" None
                          |> addResponse 403 "Forbidden in this environment" None
                          |> tag "queue"
            }
            |> fun a ->
                a.Describes(
                    fun d -> 
                        { 
                            d with 
                                Title = "Oddjob Web API"
                                Description = "Oddjob Web API contains various endpoints for checking statuses, both on system and program level.
                                               The API also provides methods for adding or deleting events, update program rights and for adding program to upload queue." })
            |> fun a -> { a with Models = a.Models @ 
                                            [
                                                typeof<OriginStream[]>
                                                typeof<string[]> 
                                                typeof<PotionStatusSummary> 
                                                typeof<PsStatusSummary>
                                                typeof<ArchivePart[]>
                                                typeof<ReadRightsInfo[]>
                                                typeof<WriteRightsInfo[]>
                                                typeof<TranscodingInfo[]>
                                                typeof<Archive>
                                            ] } 
            |> fun a -> { a with SwaggerUiPath = "/" }  
        
        api.App >=> logWithLevel LogLevel.Debug (Log.create "Suave") logFormat

    let startWebAppAsync (system : ActorSystem) oddjobConfig atvConfig akamaiConfig =
        let port = 1953us
        let serverConfig =
            { defaultConfig with
                homeFolder = Some __SOURCE_DIRECTORY__
                logger = SuaveUtils.AkkaLoggerAdapter(system, Logging.LogLevel.Debug)
                bindings = [ HttpBinding.create HTTP (IPAddress.Parse "0.0.0.0") port ] }
        startWebServerAsync serverConfig <| app system oddjobConfig atvConfig akamaiConfig

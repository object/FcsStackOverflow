namespace StackOverflow

module WebApiApp =

    open System
    open Suave
    open Suave.Filters
    open Suave.Operators
    open Suave.Swagger.FunnyDsl
    open Suave.Swagger.Swagger

    let api = 
        swagger {
        
            for route in getOf (path "/" >=> Redirection.redirect "/") do
                yield description Of route is "Swagger documentation"
                yield urlTemplate Of route is "/"
                yield route |> tag "swagger"
        
            // health 
            for route in getOf (path "/health" >=> Redirection.redirect "/") do
                yield description Of route is "Health check for the Oddjob system"
                yield urlTemplate Of route is "/health"
                yield route 
                        |> addResponse 200 "returns health status" None // actual return type causes infinite loop in suave.swagger
                        |> tag "health"
        
            // ip 
            for route in getOf (path "/ip" >=> Redirection.redirect "/") do
                yield description Of route is "Used to check geoblock information for given ip address"
                yield urlTemplate Of route is "/ip/{ipAddress}"
                yield parameter "ipAddress" Of route (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield route 
                        |> addResponse 200 "Returns informationon of geoblock of given ip address" (Some typeof<string>)
                        |> tag "ip"
            
            // status endpoints
            for route in getOf (path "/status" >=> Redirection.redirect "/") do
                yield description Of route is "Shows aggregated summary of the distribution status of a given program"
                yield urlTemplate Of route is "/status/{clientId}/{eventId}"
                yield parameter "clientId" Of route  
                    (fun p -> { p with Type = (Some typeof<string>); In=Path})
                yield parameter "eventId" Of route 
                    (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield route 
                    |> addResponse 200 "returns group summary of the event" (Some typeof<string>)
                    |> tag "status"
            
            // event endpoints   
            for route in getOf (path "/events" >=> Redirection.redirect "/") do
                yield description Of route is "Shows state of event journal."
                yield urlTemplate Of route is "/events/{segment}/{eventId}/state"
                yield parameter "segment" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield parameter "eventId" Of route 
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield route 
                        |> addResponse 200 "Returns journal state" (Some typeof<string>)
                        |> tag "events"
                                         
            for route in getOf (path "/events" >=> Redirection.redirect "/") do
                yield description Of route is "Shows journal events."
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
                        |> addResponse 200 "Returns list of journal events" (Some typeof<seq<string>>)
                        |> tag "events"
                        
           // akamai endpoints            
            for route in getOf (path "/akamai" >=> Redirection.redirect "/") do
                
                yield description Of route is "Gives information on local Akamai"
                yield urlTemplate Of route is "/akamai/local/{persistenceId}"
                yield parameter "persistenceId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield route 
                        |> addResponse 200 "Returns local Akamai information" (Some typeof<string>)
                        |> tag "akamai"
                            
            for route in getOf (path "/akamai" >=> Redirection.redirect "/") do
                
                yield description Of route is "Gives information on remote Akamai"
                yield urlTemplate Of route is "/akamai/remote/{persistenceId}"
                yield parameter "persistenceId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield route 
                        |> addResponse 200 "Returns remote Akamai information" (Some typeof<string>)
                        |> tag "akamai"
                            
            for route in getOf (path "/akamai" >=> Redirection.redirect "/") do
                
                yield description Of route is "Gives information about Akamai"
                yield urlTemplate Of route is "/akamai/{persistenceId}"
                yield parameter "persistenceId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield route 
                        |> addResponse 200 "Returns both local and remote Akamai information" (Some typeof<string>)
                        |> tag "akamai"
            
            // origin endpoints                
            for route in getOf (path "/origin" >=> Redirection.redirect "/") do
                yield description Of route is "Gives information on local Origin"
                yield urlTemplate Of route is "/origin/local/{persistenceId}"
                yield parameter "persistenceId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield route 
                        |> addResponse 200 "Returns local Origin information" (Some typeof<string>)
                        |> tag "origin"
                            
            for route in getOf (path "/origin" >=> Redirection.redirect "/") do
                yield description Of route is "Gives information on remote Origin"
                yield urlTemplate Of route is "/origin/remote/{persistenceId}"
                yield parameter "persistenceId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield route 
                        |> addResponse 200 "Returns remote Akamai information" (Some typeof<string>)
                        |> tag "origin"
                            
            for route in getOf (path "/origin" >=> Redirection.redirect "/") do
                yield description Of route is "Gives information about Origin"
                yield urlTemplate Of route is "/origin/{persistenceId}"
                yield parameter "persistenceId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield route 
                        |> addResponse 200 "Returns both local and remote Origin information" (Some typeof<string>)
                        |> tag "origin"
            
            //ps endpoints
            for route in getOf (path "/ps" >=> Redirection.redirect "/") do
                yield description Of route is "Shows information about rights for program"
                yield urlTemplate Of route is "/ps/rights/{piProgId}"
                yield parameter "piProgId" Of route  
                    (fun p -> { p with Type = (Some typeof<string>); In=Path })                
                yield route 
                    |> addResponse 200 "Returns rights info" (Some typeof<string>)
                    |> tag "ps"
                            
            for route in getOf (path "/ps" >=> Redirection.redirect "/") do
                yield description Of route is "Shows information about program files"
                yield urlTemplate Of route is "/ps/files/{piProgId}"
                yield parameter "piProgId" Of route  
                    (fun p -> { p with Type = (Some typeof<string>); In=Path })                
                yield route 
                    |> addResponse 200 "Returns list of files info" (Some typeof<string>)
                    |> tag "ps"
                            
            for route in getOf (path "/ps" >=> Redirection.redirect "/") do
                yield description Of route is "Show information about transcoding of program"
                yield urlTemplate Of route is "/ps/transcoding/{piProgId}"
                yield parameter "piProgId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })                
                yield route 
                    |> addResponse 200 "Returns list of transcoding info" (Some typeof<string>)
                    |> tag "ps"
                            
            for route in getOf (path "/ps" >=> Redirection.redirect "/") do
                yield description Of route is "Gives services information for progam"
                yield urlTemplate Of route is "/ps/service/{piProgId}"
                yield parameter "piProgId" Of route  
                    (fun p -> { p with Type = (Some typeof<string>); In=Path })                
                yield route 
                    |> addResponse 200 "Returns service information" (Some typeof<string>)
                    |> tag "ps"
                            
            for route in getOf (path "/ps" >=> Redirection.redirect "/") do
                yield description Of route is "Gives information about the archive files of the program"
                yield urlTemplate Of route is "/ps/archive/{piProgId}"
                yield parameter "piProgId" Of route  
                    (fun p -> { p with Type = (Some typeof<string>); In=Path })                
                yield route 
                    |> addResponse 200 "Returns list of archive parts" (Some typeof<string>)
                    |> tag "ps"
                
            for route in getOf (path "/ps" >=> Redirection.redirect "/") do
                yield description Of route is "Update rights for given program"
                yield urlTemplate Of route is "/ps/rights/{piProgId}"
                yield parameter "piProgId" Of route  
                    (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield parameter "rightsInfo" Of route  
                    (fun p -> { p with Type = (Some typeof<string>); In=Body })
                yield route 
                        |> addResponse 200 "Returns list of rights info" (Some typeof<string>)
                        |> tag "ps"
                
            for route in getOf (path "/ps" >=> Redirection.redirect "/") do
                yield description Of route is "Restore files for given program"
                yield urlTemplate Of route is "/ps/files/{piProgId}"
                yield parameter "piProgId" Of route  
                    (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield parameter "filesInfo" Of route  
                    (fun p -> { p with Type = (Some typeof<string>); In=Body })
                yield route 
                        |> addResponse 200 "Returns list of files info" (Some typeof<string>)
                        |> tag "ps"
          
            // potion endpoints
            for route in getOf (path "/potion" >=> Redirection.redirect "/") do
                yield description Of route is "Gets potions mapping for given persistence id"
                yield urlTemplate Of route is "/potion/mapping/{persistenceId}"
                yield parameter "persistenceId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield route 
                        |> addResponse 200 "Returns potion mapping" (Some typeof<string>)
                        |> tag "potion"
                        
            for route in getOf (path "/potion" >=> Redirection.redirect "/") do
                yield description Of route is "Gets potions publication for given persistence id"
                yield urlTemplate Of route is "/potion/publication/{persistenceId}"
                yield parameter "persistenceId" Of route  
                        (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield route 
                        |> addResponse 200 "Returns potion publication" (Some typeof<string>)
                        |> tag "potion"
                        
            // queue endpoints
            for route in getOf (path "/queues" >=> Redirection.redirect "/") do
                yield description Of route is "Service to force file upload for the given program"
                yield urlTemplate Of route is "/queues/ps/programs/{piProgId}"
                yield parameter "piProgId" Of route  
                     (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield parameter "priority" Of route  
                     (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                yield route 
                        |> addResponse 200 "Url to status end point" (Some typeof<string>)
                        |> tag "queue"
                        
            for route in getOf (path "/queues" >=> Redirection.redirect "/") do
                yield description Of route is "Service to send given change message to programs queue"
                yield urlTemplate Of route is "/queues/ps/programs"
                yield parameter "message" Of route  
                    (fun p -> { p with Type = (Some typeof<string>); In=Body })
                yield parameter "priority" Of route  
                     (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                yield route 
                        |> addResponse 200 "Url to status end point" (Some typeof<string>)
                        |> tag "queue"
                        
            for route in postOf (path "/queues" >=> Redirection.redirect "/") do
                yield description Of route is "Service to force subtitles update for the given program"
                yield urlTemplate Of route is "/queues/ps/subtitles/{piProgId}"
                yield parameter "piProgId" Of route  
                     (fun p -> { p with Type = (Some typeof<string>); In=Path })
                yield parameter "priority" Of route  
                     (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                yield route 
                        |> addResponse 200 "Url to status end point" (Some typeof<string>)
                        |> tag "queue"
                        
            for route in postOf (path "/queues" >=> Redirection.redirect "/") do
                yield description Of route is "Service to send messages of types addFile, addSubtitle, changeGeoblock, deleteGroup and deleteSubtitle to Potion queue"
                yield urlTemplate Of route is "/queues/potion"
                yield parameter "message" Of route  
                    (fun p -> { p with Type = (Some typeof<Object>); In=Body })
                yield parameter "priority" Of route  
                     (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                yield route 
                        |> addResponse 204 "Message successfully sent to queue" None
                        |> tag "queue"
                        
            for route in postOf (path "/queues" >=> Redirection.redirect "/") do
                yield description Of route is "Service to send given change message to subtitles queue"
                yield urlTemplate Of route is "/queues/ps/subtitles"
                yield parameter "message" Of route  
                    (fun p -> { p with Type = (Some typeof<string>); In=Body })
                yield parameter "priority" Of route  
                     (fun p -> { p with Type = (Some typeof<int>); In=Query; Required=false })
                yield route 
                        |> addResponse 200 "Url to status end point" (Some typeof<string>)
                        |> tag "queue"
            
            for route in postOf (path "/queues" >=> Redirection.redirect "/") do
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
                      
            for route in postOf (path "/queues" >=> Redirection.redirect "/") do
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


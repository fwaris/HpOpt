namespace HpOptimizerCore
open System.IO.Pipes
open MBrace.FsPickler
open Microsoft.ML

//helper functions for client processes to communicate with the server

type ClientMessage = Operation * AsyncReplyChannel<Operation>


module Client =

    let openPipe pipeName =
        new NamedPipeClientStream(
          ".",
          pipeName, 
          PipeDirection.InOut,
          PipeOptions.Asynchronous 
          ) 

    //send request and receive reply
    //a reply is always expected to a request
    let sendReceive pipe dataBufferR dataBufferW message (rc:AsyncReplyChannel<Operation>) = 
        async{
            do! Server.sendMessage pipe dataBufferW message
            match! Server.receiveMessage pipe dataBufferR with
            | Some op -> rc.Reply op
            | None    -> failwith "Server no reply"
       }
   
    //client agent to process message to/from server
    let agent pipe = MailboxProcessor.Start(fun inbox -> 
        let dataBufferR:byte[] = Array.zeroCreate Server.MAX_BUFFER_SIZE
        let dataBufferW:byte[] = Array.zeroCreate Server.MAX_BUFFER_SIZE
        async {
            try 
                while true do
                    let! op,rc = inbox.Receive()
                    do! sendReceive pipe dataBufferR dataBufferW op rc
            with ex -> printfn "%A" ex.Message
        }
    )

    let initServer (agent:MailboxProcessor<_>)  parms =
        async {
            let msg = Server_Init {Parms=parms}
            match! agent.PostAndAsyncReply(fun rc -> msg,rc) with
            | Client_RunParameters parms -> return parms
            | x -> return (failwithf "unexpected %A" x)
        }

    let propose (agent:MailboxProcessor<_>) count reslt =
        async {
            let msg = Server_ProposeSweeps reslt
            match! agent.PostAndAsyncReply(fun rc -> msg,rc) with
            | Client_RunParameters parms -> return parms
            | x -> return (failwithf "unexpected %A" x)
        }

    let run sweep trainerFunction = sweep |> Option.map(fun {Id=i; Parms=p} -> {Id=i; Result=p |> ParameterSet |>  trainerFunction})

    //loop to continually run the parameter search
    //search ends when server terminates the pipe
    let runModel namedPipe hyperParameters trainerFunction =
        let pipe = openPipe namedPipe
        pipe.Connect()
        let agent = agent pipe
        let mutable lastRslt = None
        async {
            try 
                    let! initSweep = initServer agent hyperParameters 
                    lastRslt <- run initSweep trainerFunction
                    while true do
                        if lastRslt.IsNone then
                            do! Async.Sleep 5000 // wait 
                        let! sweep = propose agent 1  lastRslt
                        lastRslt <- run sweep trainerFunction
            with ex -> 
                pipe.Close()
                //System.Environment.Exit(0)
            }


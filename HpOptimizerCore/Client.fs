namespace HpOptimizerCore
open System.IO.Pipes
open MBrace.FsPickler
open Microsoft.ML

type ClientMessage = Operation * AsyncReplyChannel<Operation>


module Client =

    let openPipe pipeName =
        new NamedPipeClientStream(
          ".",
          pipeName, 
          PipeDirection.InOut,
          PipeOptions.Asynchronous 
          ) 

    let sendReceive pipe dataBufferR dataBufferW message (rc:AsyncReplyChannel<Operation>) = 
        async{
            do! Server.sendMessage pipe dataBufferW message
            match! Server.receiveMessage pipe dataBufferR with
            | Some op -> rc.Reply op
            | None    -> failwith "Server no reply"
       }
   
    let agent pipe = MailboxProcessor.Start(fun inbox -> 
        let dataBufferR:byte[] = Array.zeroCreate Server.MAX_BUFFER_SIZE
        let dataBufferW:byte[] = Array.zeroCreate Server.MAX_BUFFER_SIZE
        let history = ref []
        async {
            try 
                while true do
                    let! op,rc = inbox.Receive()
                    do! sendReceive pipe dataBufferR dataBufferW op rc
            with ex -> printfn "%A" ex.Message
        }
    )

    let initServer (agent:MailboxProcessor<_>) maxHistory parms =
        async {
            let msg = Server_Init {MaxHistory=maxHistory; Parms=parms}
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

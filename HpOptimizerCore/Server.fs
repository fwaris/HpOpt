namespace HpOptimizerCore

//server to handle named pipe clients

open System.IO.Pipes
open MBrace.FsPickler
open Microsoft.ML
open Microsoft.ML.Sweeper

type HpInit      = {Parms:IValueGenerator[]}
type HpRunParms  = {Id:int; Parms:IParameterValue[]}
type HpRunResult = {Id:int; Result:IRunResult}

type Operation = 
    //to server
    | Server_Init of HpInit
    | Server_ProposeSweeps of HpRunResult option
    //to client
    | Client_RunParameters of HpRunParms option
    | Client_ServerDone

type Message = Operation * AsyncReplyChannel<bool*Operation>

module Server =

  open System.Collections.Generic
  open System.IO
  open Microsoft.ML.Sweeper

  let binarySerializer, xmlSerializer = 
    let rg = CustomPicklerRegistry()
    rg.DeclareSerializable<FloatValueGenerator>()
    rg.DeclareSerializable<DiscreteValueGenerator>()
    rg.DeclareSerializable<FloatParamArguments>()
    rg.DeclareSerializable<DiscreteParamArguments>()
    rg.DeclareSerializable<FloatParameterValue>()
    rg.DeclareSerializable<RunResult>()
    rg.DeclareSerializable<ParameterSet>()
    let cache = PicklerCache.FromCustomPicklerRegistry rg
    FsPickler.CreateBinarySerializer(picklerResolver = cache), 
    FsPickler.CreateXmlSerializer(picklerResolver = cache)

  type Pipe = NamedPipeServerStream 

  type ServerControl = Stop | RemovePipe of Pipe

  type NamedPipeServerStream with 
    member x.AsyncWaitForConnection() = Async.FromBeginEnd(x.BeginWaitForConnection,x.EndWaitForConnection)

  let MAX_BUFFER_SIZE = 16384;

  let receiveMessage (pipe:PipeStream) (buffer:byte[]) =
    async {
      let! read = pipe.AsyncRead(buffer)
      if pipe.IsConnected then
        use ms = new MemoryStream(buffer,0,read)
        let operation : Operation = binarySerializer.Deserialize(ms, leaveOpen=true)
        //printfn "server %A" msg
        return Some operation
      else
        return None
    }

  let sendMessage (pipe:PipeStream) dataBuffer (operation:Operation)  =
    async {
      use ms = new MemoryStream(dataBuffer:byte[])
      binarySerializer.Serialize<Operation>(ms,operation, leaveOpen = true)
      let sz = int (ms.Position + 1L)
      do! pipe.AsyncWrite(dataBuffer, 0, sz)
      pipe.Flush()
    }

  let echoAgent() = MailboxProcessor.Start(fun inbox -> 
    async {
      while true do
        let! ((msg, rc):Message) = inbox.Receive()
        do! Async.Sleep(100)
        rc.Reply(true,msg)
    })

  let handleClient (pipe:Pipe) (handler:MailboxProcessor<Message>) =
    let dataBufferR:byte[] = Array.zeroCreate MAX_BUFFER_SIZE
    let dataBufferW:byte[] = Array.zeroCreate MAX_BUFFER_SIZE
    let count = ref 0
    let go  = ref true
    async {
    try
        while !go && pipe.IsConnected do
            match! receiveMessage pipe dataBufferR with
            | Some msg ->
            let! cont,rplyMsg =  handler.PostAndAsyncReply(fun rc -> msg,rc)
            go := cont
            count := !count + 1
            if pipe.IsConnected then
                do! sendMessage pipe dataBufferW  rplyMsg
            if not cont then
                printfn "%A messages processed" !count
            | None -> printfn "%A messages processed" !count // client disconnected
    with ex -> 
        printfn "%s" ex.Message
    }

  //let pipeSec() =
  //  let m_ps = new PipeSecurity()
  //  let r1 = new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow)
  //  let r2 = new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow)
  //  m_ps.AddAccessRule(r1)
  //  m_ps.AddAccessRule(r2)
  //  m_ps

  let createPipe pipeName = 
    //let sec = pipeSec()
    new NamedPipeServerStream(
      pipeName, 
      PipeDirection.InOut, 
      -1, 
      PipeTransmissionMode.Message, 
      PipeOptions.Asynchronous ||| PipeOptions.WriteThrough , 
      MAX_BUFFER_SIZE, 
      MAX_BUFFER_SIZE) 
      //sec)

  let awaitConnection (agent:MailboxProcessor<ServerControl>) (pipe:NamedPipeServerStream) handler =
      async {
        try
          printfn "await connection"
          do! pipe.AsyncWaitForConnection()
          printfn "client connected"
          //spawn connection handler
          async {
            try
              do! handleClient pipe (handler())
              printfn "done handler"
              pipe.Close()
              agent.Post (RemovePipe pipe)
            with ex -> 
              agent.Post(RemovePipe pipe)
              printfn "%s" ex.Message
          }
          |> Async.Start
        with ex -> 
          printfn "%s" ex.Message
      }
    
  let startServer pipeName handler =
    let allPipes = HashSet<Pipe>()
    let running = ref true

    let closeAll() = lock allPipes (fun () -> allPipes |> Seq.iter (fun p->p.Close()))
    let removePipe pipe = lock allPipes (fun () -> allPipes.Remove pipe |> ignore)

    let agent = MailboxProcessor.Start (fun inbox ->
      let rec agentLoop() =
        async {
          let! msg = inbox.Receive()
          match msg with
          | Stop -> 
            running := false
            closeAll()
          | RemovePipe pipe ->
              removePipe pipe
              printfn "pipe removed"
              return! agentLoop()
        }
      agentLoop())
  
    let rec loop() =
      async {
        if !running then
          let pipe = createPipe pipeName
          lock allPipes (fun () -> allPipes.Add pipe |> ignore)
          do! awaitConnection agent pipe handler
          return! loop()
      }
  
    loop() |> Async.Start
    agent


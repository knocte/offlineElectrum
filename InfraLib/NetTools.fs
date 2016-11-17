namespace Fsx.Infrastructure

module NetTools =

    let private IsMonoTlsProblemException(exceptionType: Type): bool =
        exceptionType.FullName = "Mono.Security.Protocol.Tls.TlsException"

    let rec private SomeAreWebException(aseq: seq<Type>): bool =
        Seq.exists (fun exType -> (exType = typedefof<WebException>)) aseq

    (** TODO: add tests and simplify the below functions related to IsMonoTlsProblem **)
    let private RemoveFirst(aseq: seq<Type>): seq<Type> =
        Seq.skip(1) aseq

    let private RemoveLast(aseq: seq<Type>): seq<Type> =
        let count = aseq.Count()
        Seq.take(count - 1) aseq

    let rec private ToChain (ex: Exception): seq<Type> =
        if (ex = null) then
            Seq.empty<Type>
        else
            seq { yield ex.GetType(); yield! ToChain(ex.InnerException) }

(* THE BEAST THAT WE'RE TRYING TO RECOGNIZE BELOW:

 System.AggregateException: One or more errors occurred. ---> System.Net.WebException: Error: SendFailure (Error writing headers) ---> System.Net.WebException: Error writing headers ---> System.IO.IOException: The authentication or decryption has failed. ---> Mono.Security.Protocol.Tls.TlsException: The authentication or decryption has failed.
  at Mono.Security.Protocol.Tls.RecordProtocol.EndReceiveRecord (IAsyncResult asyncResult) <0x41179bd0 + 0x0010b> in <filename unknown>:0 
  at Mono.Security.Protocol.Tls.SslClientStream.SafeEndReceiveRecord (IAsyncResult ar, Boolean ignoreEmpty) <0x41179b10 + 0x0002b> in <filename unknown>:0 
  at Mono.Security.Protocol.Tls.SslClientStream.NegotiateAsyncWorker (IAsyncResult result) <0x41176970 + 0x00227> in <filename unknown>:0 
  --- End of inner exception stack trace ---
  at System.Net.WebConnection.EndWrite (System.Net.HttpWebRequest request, Boolean throwOnError, IAsyncResult result) <0x4117b620 + 0x00207> in <filename unknown>:0 
  at System.Net.WebConnectionStream+<SetHeadersAsync>c__AnonStorey1.<>m__0 (IAsyncResult r) <0x4117af20 + 0x0013b> in <filename unknown>:0 
  --- End of inner exception stack trace ---
  --- End of inner exception stack trace ---
  at System.Net.HttpWebRequest.EndGetResponse (IAsyncResult asyncResult) <0x4117c6b0 + 0x0019f> in <filename unknown>:0 
  at System.Net.WebClient.GetWebResponse (System.Net.WebRequest request, IAsyncResult result) <0x4117c630 + 0x00028> in <filename unknown>:0 
  at System.Net.WebClient.DownloadBitsResponseCallback (IAsyncResult result) <0x4117c190 + 0x000cb> in <filename unknown>:0 
  --- End of inner exception stack trace ---
  at System.Threading.Tasks.Task.ThrowIfExceptional (Boolean includeTaskCanceledExceptions) <0x7fdc4949d920 + 0x00037> in <filename unknown>:0 
  at System.Threading.Tasks.Task.Wait (Int32 millisecondsTimeout, CancellationToken cancellationToken) <0x7fdc4949ed90 + 0x000c7> in <filename unknown>:0 
  at System.Threading.Tasks.Task.Wait () <0x7fdc4949ec80 + 0x00028> in <filename unknown>:0 
  at FSI_0005.Gatecoin.Infrastructure.NetTools.DownloadFile (System.Uri uri) <0x4112f840 + 0x00166> in <filename unknown>:0 
  at <StartupCode$FSI_0006>.$FSI_0006.main@ () <0x41122e40 + 0x00327> in <filename unknown>:0 
  at (wrapper managed-to-native) System.Reflection.MonoMethod:InternalInvoke (System.Reflection.MonoMethod,object,object[],System.Exception&)
  at System.Reflection.MonoMethod.Invoke (System.Object obj, BindingFlags invokeAttr, System.Reflection.Binder binder, System.Object[] parameters, System.Globalization.CultureInfo culture) <0x7fdc495ab9e0 + 0x000a1> in <filename unknown>:0 
---> (Inner Exception #0) System.Net.WebException: Error: SendFailure (Error writing headers) ---> System.Net.WebException: Error writing headers ---> System.IO.IOException: The authentication or decryption has failed. ---> Mono.Security.Protocol.Tls.TlsException: The authentication or decryption has failed.
  at Mono.Security.Protocol.Tls.RecordProtocol.EndReceiveRecord (IAsyncResult asyncResult) <0x41179bd0 + 0x0010b> in <filename unknown>:0 
  at Mono.Security.Protocol.Tls.SslClientStream.SafeEndReceiveRecord (IAsyncResult ar, Boolean ignoreEmpty) <0x41179b10 + 0x0002b> in <filename unknown>:0 
  at Mono.Security.Protocol.Tls.SslClientStream.NegotiateAsyncWorker (IAsyncResult result) <0x41176970 + 0x00227> in <filename unknown>:0 
  --- End of inner exception stack trace ---
  at System.Net.WebConnection.EndWrite (System.Net.HttpWebRequest request, Boolean throwOnError, IAsyncResult result) <0x4117b620 + 0x00207> in <filename unknown>:0 
  at System.Net.WebConnectionStream+<SetHeadersAsync>c__AnonStorey1.<>m__0 (IAsyncResult r) <0x4117af20 + 0x0013b> in <filename unknown>:0 
  --- End of inner exception stack trace ---
  --- End of inner exception stack trace ---
  at System.Net.HttpWebRequest.EndGetResponse (IAsyncResult asyncResult) <0x4117c6b0 + 0x0019f> in <filename unknown>:0 
  at System.Net.WebClient.GetWebResponse (System.Net.WebRequest request, IAsyncResult result) <0x4117c630 + 0x00028> in <filename unknown>:0 
  at System.Net.WebClient.DownloadBitsResponseCallback (IAsyncResult result) <0x4117c190 + 0x000cb> in <filename unknown>:0 <---

*)
    let private IsMonoTlsProblem (ex: Exception): bool =
        let chain = ToChain(ex)
        let isFirstExceptionAnAggregate = (chain.First() = typedefof<AggregateException>)
        let chainInBetween =
            if isFirstExceptionAnAggregate then
                RemoveLast(RemoveFirst(chain))
            else
                RemoveLast(chain)
        let someExceptionsInBetweenAreWebExceptions = SomeAreWebException(chainInBetween)
        isFirstExceptionAnAggregate && someExceptionsInBetweenAreWebExceptions && IsMonoTlsProblemException(chain.Last())

    let DownloadFileWithWGet (uri: Uri): unit =
        ProcessTools.SafeExec("wget", String.Format("--output-document={0} {1}", Path.GetFileName(uri.LocalPath), uri.ToString()), true) |> ignore

    let DownloadFile (uri: Uri): unit =
        use webClient = new WebClient()
        let mutable firstProgressEvent = true
        let onProgress (progressEventArgs: DownloadProgressChangedEventArgs) =
            if (firstProgressEvent) then
                Console.WriteLine ("Starting download of {0}MB...", (progressEventArgs.TotalBytesToReceive / 1000000L))
            firstProgressEvent <- false

        webClient.DownloadProgressChanged.Subscribe onProgress |> ignore
        let task = webClient.DownloadFileTaskAsync (uri, Path.GetFileName(uri.LocalPath))
        task.Wait()

    let private DownloadFileIgnoringSslCertificates (uri: Uri): unit =
        ServicePointManager.ServerCertificateValidationCallback <- System.Net.Security.RemoteCertificateValidationCallback(fun _ _ _ _ -> true)
        try
            DownloadFile(uri)
        with
            | ex when IsMonoTlsProblem(ex) ->
                Console.Error.WriteLine("Falling back to WGET download")
                DownloadFileWithWGet(uri)

    let SafeDownloadFile (uri: Uri, sha256sum: string): FileInfo =
        let resultFile = new FileInfo(Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileName(uri.LocalPath)))

        if (resultFile.Exists) then
            Console.WriteLine("Already downloaded")
        else
            Console.WriteLine ("File '{0}' not found, going to start download...", resultFile.Name)
            try
                DownloadFile(uri)
                Console.WriteLine("Download finished")
            with
                | ex when IsMonoTlsProblem(ex) ->
                    Console.Error.WriteLine("Falling back to certificate-less safe download")
                    DownloadFileIgnoringSslCertificates(uri)

        if not (sha256sum = MiscTools.CalculateSHA256(resultFile)) then
            failwith("SHA256 hash doesn't match, beware possible M.I.T.M.A.: Man In The Middle Attack")
        resultFile
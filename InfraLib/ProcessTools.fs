
namespace Fsx.Infrastructure

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Diagnostics
open System.Threading
open System.Linq
open System.Text
open System.Security.Cryptography

open MiscTools

module ProcessTools =

    type OutChunk = StdOut of StringBuilder | StdErr of StringBuilder
    type OutputBuffer = list<OutChunk>
    type ProcessResult = { ExitCode: int; Output: OutputBuffer }
    type Standard =
        | Output
        | Error
        override self.ToString() =
            sprintf "%A" self

    type ProcessDetails =
        { Command: string; Arguments: string }
        override self.ToString() =
            sprintf "Command: %s. Arguments: %s." self.Command self.Arguments

    type ProcessCouldNotStart(procDetails: ProcessDetails, innerException: Exception) =
        inherit Exception(sprintf
            "Process could not start! %s" (procDetails.ToString()),
            innerException)

    let Execute (procDetails: ProcessDetails, echo: bool, hidden: bool)
        : ProcessResult =

        // I know, this shit below is mutable, but it's a consequence of dealing with .NET's Process class' events?
        let mutable outputBuffer: list<OutChunk> = []
        let outputBufferLock = new Object()

        if (echo) then
            Console.WriteLine(sprintf "%s %s" procDetails.Command procDetails.Arguments)

        let startInfo = new ProcessStartInfo(procDetails.Command, procDetails.Arguments)
        startInfo.UseShellExecute <- false
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        use proc = new System.Diagnostics.Process()
        proc.StartInfo <- startInfo

        let ReadStandard(std: Standard) =

            let print =
                match std with
                | Standard.Output -> Console.Write : char array -> unit
                | Standard.Error -> Console.Error.Write

            let flush =
                match std with
                | Standard.Output -> Console.Out.Flush
                | Standard.Error -> Console.Error.Flush

            let outputToReadFrom =
                match std with
                | Standard.Output -> proc.StandardOutput
                | Standard.Error -> proc.StandardError

            let EndOfStream(readCount: int): bool =
                if (readCount > 0) then
                    false
                else if (readCount < 0) then
                    true
                else //if (readCount = 0)
                    outputToReadFrom.EndOfStream

            let ReadIteration(): bool =

                // I want to hardcode this to 1 because otherwise the order of the stderr|stdout
                // chunks in the outputbuffer would innecessarily depend on this bufferSize, setting
                // it to 1 makes it slow but then the order is only relying (in theory) on how the
                // streams come and how fast the .NET IO processes them
                let outChar = [|'x'|] // 'x' is a dummy value that will get replaced
                let bufferSize = 1
                let uniqueElementIndexInTheSingleCharBuffer = bufferSize - 1

                if not (outChar.Length = bufferSize) then
                    failwith "Buffer Size must equal current buffer size"

                let readTask = outputToReadFrom.ReadAsync(outChar, uniqueElementIndexInTheSingleCharBuffer, bufferSize)
                readTask.Wait()
                if not (readTask.IsCompleted) then
                    failwith "Failed to read"

                let readCount = readTask.Result
                if (readCount > bufferSize) then
                    failwith "StreamReader.Read() should not read more than the bufferSize if we passed the bufferSize as a parameter"

                if (readCount = bufferSize) then
                    if not (hidden) then
                        print outChar
                        flush()

                    lock outputBufferLock (fun _ ->

                        let leChar = outChar.[uniqueElementIndexInTheSingleCharBuffer]
                        match outputBuffer with
                        | [] ->
                            let newBuilder = new StringBuilder(leChar.ToString())
                            let newBlock =
                                match std with
                                | Standard.Output -> StdOut(newBuilder)
                                | Standard.Error -> StdErr(newBuilder)
                            outputBuffer <- [ newBlock ]
                        | head::tail ->
                            match head with
                            | StdOut(out) ->
                                match std with
                                | Standard.Output ->
                                    out.Append(outChar) |> ignore
                                | Standard.Error ->
                                    let newErrBuilder = StdErr(new StringBuilder(leChar.ToString()))
                                    outputBuffer <- newErrBuilder::outputBuffer
                            | StdErr(err) ->
                                match std with
                                | Standard.Error ->
                                    err.Append(outChar) |> ignore
                                | Standard.Output ->
                                    let newOutBuilder = StdOut(new StringBuilder(leChar.ToString()))
                                    outputBuffer <- newOutBuilder::outputBuffer
                    )

                let continueIterating = not(EndOfStream(readCount))
                continueIterating

            // this is a way to do a `do...while` loop in F#...
            while (ReadIteration()) do
                ignore None

        let outReaderThread = new Thread(new ThreadStart(fun _ ->
            ReadStandard(Standard.Output)
        ))

        let errReaderThread = new Thread(new ThreadStart(fun _ ->
            ReadStandard(Standard.Error)
        ))

        try
            proc.Start() |> ignore
        with
        | e -> raise(ProcessCouldNotStart(procDetails, e))

        outReaderThread.Start()
        errReaderThread.Start()
        proc.WaitForExit()
        let exitCode = proc.ExitCode

        outReaderThread.Join()
        errReaderThread.Join()

        { ExitCode = exitCode; Output = outputBuffer }

    // TODO: move to OutputBuffer type
    let rec GetStdOut (outputBuffer: OutputBuffer) =
        match outputBuffer with
        | [] -> new StringBuilder()
        | head::tail ->
            match head with
            | StdOut(out) ->
                GetStdOut(tail).Append(out.ToString())
            | _ ->
                GetStdOut(tail)

    // TODO: move to OutputBuffer type
    let rec GetStdErr (outputBuffer: OutputBuffer) =
        match outputBuffer with
        | [] -> new StringBuilder()
        | head::tail ->
            match head with
            | StdErr(err) ->
                GetStdErr(tail).Append(err.ToString())
            | _ ->
                GetStdErr(tail)

    let HiddenExec (command: string, arguments: string) =
        let result = Execute({ Command = command; Arguments = arguments }, false, true)
        (result.ExitCode, GetStdOut(result.Output).ToString(), GetStdErr(result.Output).ToString())

    let Exec (command: string, arguments: string, echo: bool) =
        let result = Execute({ Command = command; Arguments = arguments }, echo, false)
        result.ExitCode

    exception ProcessFailed of string

    let SafeHiddenExec (command: string, arguments: string) =
        let exitCode,stdOut,stdErr = HiddenExec(command, arguments)
        if not (exitCode = 0) then
            Console.WriteLine(stdOut)
            Console.Error.WriteLine(stdErr)
            Console.Error.WriteLine()
            raise(ProcessFailed(String.Format("Command '{0}' failed with exit code {1}. Arguments supplied: '{2}'",
                                              command, exitCode.ToString(), arguments)))
        exitCode,stdOut,stdErr

    let SafeExec (command: string, arguments: string, echo: bool) =
        let exitCode = Exec(command, arguments, echo)
        if not (exitCode = 0) then
            raise(ProcessFailed(String.Format("Command '{0}' failed with exit code {1}. Arguments supplied: '{2}'",
                                              command, exitCode.ToString(), arguments)))
        ()

    let rec private ExceptionIsOfTypeOrIncludesAnyInnerExceptionOfType(ex: Exception, t: Type): bool =
        if (ex = null) then
            false
        else if (ex.GetType() = t) then
            true
        else
            ExceptionIsOfTypeOrIncludesAnyInnerExceptionOfType(ex.InnerException, t)

    let rec private CheckIfCommandWorksInShellWithWhich (command: string): bool =
        let WhichCommandWorksInShell (): bool =
            let maybeResult =
                try
                    Some(HiddenExec("which", String.Empty))
                with
                | ex when (ExceptionIsOfTypeOrIncludesAnyInnerExceptionOfType(ex, typeof<System.ComponentModel.Win32Exception>))
                    -> None
                | _ -> reraise()

            match maybeResult with
            | None -> false
            | Some(_) -> true

        if not (WhichCommandWorksInShell()) then
            failwith "'which' doesn't work, please install it first"

        let exitCode,_,_ = HiddenExec("which", command)
        match exitCode with
        | 0 -> true
        | _ -> false

    let private HasWindowsExecutableExtension(path: string) =
        //FIXME: should do it in a case-insensitive way
        path.EndsWith(".exe") ||
            path.EndsWith(".bat") ||
            path.EndsWith(".cmd") ||
            path.EndsWith(".com")

    let private IsFileInWindowsPath(command: string) =
        let pathEnvVar = Environment.GetEnvironmentVariable("PATH")
        let paths = pathEnvVar.Split(Path.PathSeparator)
        paths.Any(fun path -> File.Exists(Path.Combine(path, command)))

    let CommandWorksInShell (command: string): bool =
        if (MiscTools.GuessPlatform() = MiscTools.Platform.Windows) then
            let exists = File.Exists(command) || IsFileInWindowsPath(command)
            if (exists && HasWindowsExecutableExtension(command)) then
                true
            else
                false
        else
            CheckIfCommandWorksInShellWithWhich(command)



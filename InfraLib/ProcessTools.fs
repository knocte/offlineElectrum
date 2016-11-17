namespace Fsx.Infrastructure

module ProcessTools =

    let HiddenExec (command: string, arguments: string) =
        let startInfo = new System.Diagnostics.ProcessStartInfo(command)
        startInfo.Arguments <- arguments
        startInfo.UseShellExecute <- false

        // equivalent to `>/dev/null 2>&1` in unix
        startInfo.RedirectStandardError <- true
        startInfo.RedirectStandardOutput <- true

        use proc = System.Diagnostics.Process.Start(startInfo)
        proc.WaitForExit()
        (proc.ExitCode,proc.StandardOutput.ReadToEnd(),proc.StandardError.ReadToEnd())

    let Exec (command: string, arguments: string, echo: bool) =
        let psi = new System.Diagnostics.ProcessStartInfo(command)

        psi.Arguments <- arguments
        psi.UseShellExecute <- false
        if (echo) then
            Console.WriteLine("{0} {1}", command, arguments)
        let p = System.Diagnostics.Process.Start(psi)
        p.WaitForExit()
        p.ExitCode

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

    let private NonBlockingCommandWorksInShell (command: string, args: string): bool =
        let exitCode =
            try
                let exitCode,_,_ = HiddenExec(command, args)
                Some(exitCode)
            with
                | :? System.ComponentModel.Win32Exception -> (); None
        if exitCode.IsNone then
            false
        else
            true

    let private WhichCommandWorksInShell (): bool =
        NonBlockingCommandWorksInShell("which", String.Empty)

    let CommandWorksInShell (command: string): bool =
        if not (WhichCommandWorksInShell()) then
            failwith "'which' doesn't work, please install it first"
        NonBlockingCommandWorksInShell("which", command)
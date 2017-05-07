
namespace Fsx.Infrastructure

module UnixTools =

    let mutable firstTimeSudoIsRun = true
    let private SudoInternal (command: string, safe: bool): Option<int> =
        if not (ProcessTools.CommandWorksInShell "id") then
            Console.WriteLine ("'id' unix command is needed for this script to work")
            Environment.Exit(2)

        let _,idOutput,_ = ProcessTools.HiddenExec("id","-u")
        if (idOutput.Trim() = "0") then
            Console.Error.WriteLine ("Error: sudo privileges detected. Please don't run this directly with sudo or with the root user.")
            Environment.Exit(3)

        if (firstTimeSudoIsRun) then
            ProcessTools.SafeExec("sudo", "-k", true)

        Console.WriteLine("Attempting sudo for '{0}'", command)
        let result =
            if (safe) then
                ProcessTools.SafeExec("sudo", command, true)
                None
            else
                Some(ProcessTools.Exec("sudo", command, true))
        firstTimeSudoIsRun <- false
        result

    let UnsafeSudo(command: string) =
        let res = SudoInternal(command, false)
        if (res.IsNone) then
            failwith "Abnormal None result from SudoInternal(_,false)"
        res.Value

    let Sudo(command: string): unit =
        SudoInternal(command, true) |> ignore

    type AptPackage =
    | Missing
    | ExistingVersion of string
    let IsAptPackageInstalled(packageName: string): AptPackage =
        if not (ProcessTools.CommandWorksInShell "dpkg") then
            Console.Error.WriteLine ("This script is only for debian-based distros, aborting.")
            Environment.Exit(3)

        let dpkgSearchExitCode,dpkgOutput,_ = ProcessTools.HiddenExec("dpkg", String.Format("-s {0}", packageName))
        if not (dpkgSearchExitCode = 0) then
            AptPackage.Missing
        else
            let dpkgLines = dpkgOutput.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
            let versionTag = "Version: "
            let maybeVersion = dpkgLines.Where(fun line -> line.StartsWith(versionTag)).Single()
            let version = maybeVersion.Substring(versionTag.Length)
            AptPackage.ExistingVersion(version)

    let InstallAptPackageIfNotAlreadyInstalled(packageName: string)=
        if (IsAptPackageInstalled(packageName) = AptPackage.Missing) then
            Console.WriteLine("Installing {0}...", packageName)
            Sudo(String.Format("apt -y install {0}", packageName)) |> ignore

    let rec DownloadAptPackage (packageName: string) =
        let exitCode,output,err = ProcessTools.HiddenExec("apt", "download " + packageName)
        if (exitCode = 0) then
            Console.WriteLine("Downloaded " + packageName)
            ()
        else if (err.Contains("E: Can't select candidate version from package")) then
            Console.WriteLine("Virtual package '{0}' found, provided by:", packageName)
            InstallAptPackageIfNotAlreadyInstalled("aptitude")
            let exitCode,output,err = ProcessTools.SafeHiddenExec("aptitude", "show " + packageName)
            let lines = output.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
            for line in lines do
                if (line.StartsWith("Provided by:")) then
                    Console.WriteLine(line.Substring("Provided by:".Length))
                    Console.Write("Choose the package from the list above: ")
                    let pkg = Console.ReadLine()
                    DownloadAptPackage(pkg)
        else
            Console.WriteLine(output)
            Console.Error.WriteLine(err)
            failwith(err)

    let DownloadAptPackageRecursively (packageName: string) =
        InstallAptPackageIfNotAlreadyInstalled("apt-rdepends")
        let _,output,_ = ProcessTools.SafeHiddenExec("apt-rdepends", packageName)
        let lines = output.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
        for line in lines do
            if not (line.Trim().Contains("Depends:")) then
                DownloadAptPackage(line.Trim())

    let DownloadAptPackagesRecursively (packages: string seq) =
        for pkg in packages do
            DownloadAptPackageRecursively(pkg)
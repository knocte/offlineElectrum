namespace Fsx.Infrastructure

module MiscTools =

type private SupportedCheckSumAlgorithm =
        MD5 | SHA256

    let private ComputeHash(algo: SupportedCheckSumAlgorithm, stream: Stream) =
        match algo with
        | SupportedCheckSumAlgorithm.MD5 ->
            use md5 = MD5.Create()
            md5.ComputeHash(stream)
        | SupportedCheckSumAlgorithm.SHA256 ->
            use sha256 = new SHA256Managed()
            sha256.ComputeHash(stream)

    let private CalculateSum(algo: SupportedCheckSumAlgorithm, file: FileInfo) =
        file.Refresh()
        if not (file.Exists) then
            raise (new FileNotFoundException("File not found", file.FullName))
        use stream = File.OpenRead(file.FullName)
        let bytes = ComputeHash(algo, stream)
        BitConverter.ToString(bytes).Replace("-", String.Empty).ToLower()

    let CalculateMD5 (file: FileInfo): string =
        CalculateSum(SupportedCheckSumAlgorithm.MD5, file)

    let CalculateSHA256 (file: FileInfo): string =
        CalculateSum(SupportedCheckSumAlgorithm.SHA256, file)
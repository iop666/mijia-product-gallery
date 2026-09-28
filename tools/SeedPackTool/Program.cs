using MijiaProductGallery.SeedPackTool;

// seedpack-v1 打包工具：把 mijia-product-icons 快照转为应用初始数据包。
// 用法：dotnet run --project tools/SeedPackTool -- --icons <仓库检出根> --out <输出目录>
//        --date <yyyy-MM-dd> [--aux <aux补充包解压目录>]
if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("""
        用法: SeedPackTool --icons <mijia-product-icons检出根> --out <输出目录> --date <yyyy-MM-dd> [--aux <目录>]
        产物: <输出目录>\seed-<date>.zip （seedpack-v1：manifest.json + products.jsonl + images/）
        """);
    return 2;
}

string? icons = null, output = null, date = null, aux = null;
for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--icons":
            icons = args[i + 1];
            break;
        case "--out":
            output = args[i + 1];
            break;
        case "--date":
            date = args[i + 1];
            break;
        case "--aux":
            aux = args[i + 1];
            break;
    }
}

if (icons is null || output is null || date is null)
{
    Console.Error.WriteLine("缺少必填参数：--icons / --out / --date");
    return 2;
}

var result = await SeedPackBuilder.BuildAsync(
    new SeedPackBuildOptions
    {
        IconsDirectory = icons,
        AuxDirectory = aux,
        OutputDirectory = output,
        SnapshotDate = date,
        Source = $"iop666/mijia-product-icons@{date}",
    },
    new Progress<string>(message => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}")));

Console.WriteLine($"""
    打包完成：{result.PackagePath}
    产品 {result.ProductCount}（有图 {result.WithImage}，缺图 {result.MissingImage}，下架 {result.Delisted}）
    图片文件 {result.ImageFileCount}（含 .old {result.OldImageCount}）
    包大小 {result.PackageBytes / 1024.0 / 1024.0:F1} MB，耗时 {result.Elapsed.TotalSeconds:F1}s
    """);
return 0;

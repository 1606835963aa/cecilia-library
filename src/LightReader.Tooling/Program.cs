using LightReader.Core;
var project=Path.GetFullPath(args[0]);
var store=new ComponentStore(project,Path.Combine(project,"data","component-manifest.json"));
foreach(var id in args.Skip(1))
{
    var folder=await store.InstallAsync(id,null,CancellationToken.None);
    Console.WriteLine($"{id}: {folder}, {store.InstalledBytes(id)} bytes");
}

using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using LightReader.Core;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

// Only the parent owns this redirected input/output pair. No network listener,
// document cache or persistent worker profile is created.
if(args.Length!=2||!int.TryParse(args[0],out var parentId)||!long.TryParse(args[1],out var parentTicks))return 2;
Process parent;
try {parent=Process.GetProcessById(parentId);if(parent.StartTime.ToUniversalTime().Ticks!=parentTicks)return 2;}
catch(ArgumentException){return 2;}
IRandomAccessStream? source=null;
PdfDocument? document=null;
_ = Task.Run(async()=>{try{await parent.WaitForExitAsync();}catch(ObjectDisposedException){return;}try{source?.Dispose();}finally{Environment.Exit(0);}});
using var input=new StreamReader(Console.OpenStandardInput());
using var output=Console.OpenStandardOutput();
try
{
    while(await input.ReadLineAsync() is { } line)
    {
        try
        {
            if(line.Length>131072)throw new InvalidDataException();
            var command=JsonSerializer.Deserialize<PdfWorkerCommand>(line)??throw new InvalidDataException();
            if(command.Operation=="open")
            {
                if(document!=null||command.Path==null)throw new InvalidDataException();
                var file=await StorageFile.GetFileFromPathAsync(Path.GetFullPath(command.Path));
                source=await file.OpenAsync(FileAccessMode.Read);
                document=await PdfDocument.LoadFromStreamAsync(source);
                if(document.PageCount==0)throw new InvalidDataException();
                await PdfWorkerProtocol.WriteAsync(output,new(true,PageCount:document.PageCount),ReadOnlyMemory<byte>.Empty,CancellationToken.None);
            }
            else if(command.Operation=="render"&&document!=null&&command.Page<document.PageCount&&command.Width is >=1 and <=3200)
            {
                using var page=document.GetPage(command.Page);
                var size=PdfRenderBudget.ForPage(page.Size.Width,page.Size.Height,command.Width);
                using var image=new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(image,new PdfPageRenderOptions{DestinationWidth=size.Width,DestinationHeight=size.Height});
                if(image.Size>PdfWorkerProtocol.MaximumImageBytes)throw new InvalidDataException();
                image.Seek(0);using var buffer=new MemoryStream((int)image.Size);
                using(var bytes=image.AsStreamForRead())await bytes.CopyToAsync(buffer);
                await PdfWorkerProtocol.WriteAsync(output,new(true,PageWidth:page.Size.Width,PageHeight:page.Size.Height,RenderWidth:size.Width,RenderHeight:size.Height,ImageLength:(int)buffer.Length),buffer.GetBuffer().AsMemory(0,(int)buffer.Length),CancellationToken.None);
            }
            else if(command.Operation=="close")
            {
                document=null;source?.Dispose();source=null;
                await PdfWorkerProtocol.WriteAsync(output,new(true),ReadOnlyMemory<byte>.Empty,CancellationToken.None);
                return 0;
            }
            else throw new InvalidDataException();
        }
        catch(Exception)
        {
            var message=document==null?"无法读取 PDF，文件可能已加密或损坏":"PDF 页面绘制失败";
            document=null;source?.Dispose();source=null;
            await PdfWorkerProtocol.WriteAsync(output,new(false,Error:message),ReadOnlyMemory<byte>.Empty,CancellationToken.None);
            return 1;
        }
    }
    return 0;
}
finally {document=null;source?.Dispose();parent.Dispose();}

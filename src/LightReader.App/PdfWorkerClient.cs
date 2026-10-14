using System.Diagnostics;
using System.Text.Json;
using LightReader.Core;

namespace LightReader.App;

internal sealed class PdfWorkerClient : IDisposable
{
    private readonly Process process;
    private readonly SemaphoreSlim commands=new(1,1);
    private readonly CancellationTokenSource lifetime=new();
    private readonly CancellationTokenSource transport=new();
    private int disposed;
    private volatile bool exited;
    private volatile bool released;
    internal event Action? Failed;
    internal bool IsAlive=>!exited;
    internal bool IsReleased=>released;
    internal int Id{get;}
    internal Task Released{get;private set;}=Task.CompletedTask;

    internal PdfWorkerClient()
    {
        var executable=Path.Combine(AppContext.BaseDirectory,"LightReader.PdfWorker.exe");
        if(!File.Exists(executable))throw new IOException("PDF 阅读组件缺失，请重新安装程序");
        using var parent=Process.GetCurrentProcess();
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString());
        process=Process.Start(start)??throw new IOException("无法启动 PDF 阅读组件");
        Id=process.Id;
        process.Exited+=(_,_)=>{exited=true;if(Volatile.Read(ref disposed)==0)Failed?.Invoke();};
        process.EnableRaisingEvents=true;
        _=DiscardErrorsAsync();
    }
    private async Task DiscardErrorsAsync()
    {try{await process.StandardError.BaseStream.CopyToAsync(Stream.Null).ConfigureAwait(false);}catch(Exception error)when(error is IOException or ObjectDisposedException){} }

    internal async Task<PdfWorkerFrame> SendAsync(PdfWorkerCommand command,CancellationToken token)
    {
        using var waiting=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
        await commands.WaitAsync(waiting.Token).ConfigureAwait(false);
        try
        {
            waiting.Token.ThrowIfCancellationRequested();
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(transport.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            // Once sent, drain the entire response even if the page was superseded.
            // This keeps the next command aligned with the binary response stream.
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(command).AsMemory(),timeout.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
            var frame=await PdfWorkerProtocol.ReadAsync(process.StandardOutput.BaseStream,timeout.Token).ConfigureAwait(false);
            waiting.Token.ThrowIfCancellationRequested();
            if(!frame.Reply.Ok)throw new IOException(frame.Reply.Error??"PDF 页面绘制失败");
            return frame;
        }
        catch(OperationCanceledException)when(!waiting.IsCancellationRequested)
        {Dispose();throw new IOException("PDF 页面处理超时，请重新打开文档");}
        catch(Exception error)when(error is IOException or System.Text.Json.JsonException or ObjectDisposedException or InvalidOperationException)
        {
            var cancelled=waiting.IsCancellationRequested;
            Dispose();
            if(cancelled)throw new OperationCanceledException(waiting.Token);
            throw new IOException("PDF 阅读组件已停止，请重新打开文档",error);
        }
        finally {commands.Release();}
    }
    internal void StopForVerification()=>process.Kill();
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        Failed=null;lifetime.Cancel();
        Released=ReleaseAsync();
    }
    private async Task ReleaseAsync()
    {
        using var closing=new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            // Finish an in-flight response, then explicitly close the WinRT source
            // before exiting. Process-exit notification alone is not a close ACK.
            await commands.WaitAsync(closing.Token).ConfigureAwait(false);
            try
            {
                if(!process.HasExited)
                {
                    await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new PdfWorkerCommand("close")).AsMemory(),closing.Token).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(closing.Token).ConfigureAwait(false);
                    var reply=await PdfWorkerProtocol.ReadAsync(process.StandardOutput.BaseStream,closing.Token).ConfigureAwait(false);
                    if(!reply.Reply.Ok)throw new IOException("PDF 文件关闭失败");
                }
            }
            finally {commands.Release();}
            await process.WaitForExitAsync(closing.Token).ConfigureAwait(false);
        }
        catch(Exception error)when(error is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or JsonException){}
        finally
        {
            transport.Cancel();
            try
            {
                try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}
                await process.WaitForExitAsync().ConfigureAwait(false);exited=true;released=true;
            }
            finally {process.Dispose();}
        }
    }
}

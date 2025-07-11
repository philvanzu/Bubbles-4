using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Bubbles4.Services;
using Bubbles4.ViewModels;
using SharpCompress.Archives;

namespace Bubbles4.Models;

public class BookArchive : BookBase
{
    private string _coverKey; 
    private Task<List<Page>>? _pagesListTask = null;
    private readonly SemaphoreSlim _pagesListLock = new(1, 1);
    public BookArchive(string? path, string name, int pageCount, DateTime lastModified, DateTime created)
        : base(path, name, lastModified, pageCount, created) { }


    
    private (IArchive archive, FileStream stream)? TryOpenArchive()
    {
        IArchive archive = null;
        FileStream stream = null;
        try
        {
            stream = new FileStream(Path!, FileMode.Open, FileAccess.Read, FileShare.Read);
            archive = ArchiveFactory.Open(stream);
            return (archive, stream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cannot open {Path} : {ex.ToString()}");
            archive?.Dispose();
            stream?.Dispose();
            return null;
        }
        
    }

    private void CloseArchive(IArchive? archive, FileStream? stream)
    {
        try
        {
            archive?.Dispose();
            stream?.Dispose();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }
    public async Task<List<Page>>? GetPagesListAsync()
    {    
        List<Page> pages = null;
        await FileIOThrottler.WaitAsync();
        IArchive archive = null;
        FileStream stream = null;
        try
        {
            var pair = TryOpenArchive();
            if (pair == null)
                throw (new FileLoadException($"Could not open archive file : {Path}"));
            
            (archive, stream) = pair.Value;

            pages = archive.Entries
                .Where(e => !e.IsDirectory && FileTypes.IsImage(System.IO.Path.GetExtension(e.Key)))
                .Select(entry => new Page()
                {
                    Name = System.IO.Path.GetFileName(entry.Key),
                    Path = entry.Key,
                    //Size = entry.Size,
                    Created = entry.CreatedTime ?? Created,
                    LastModified = entry.LastModifiedTime ?? LastModified,
                    //LastAccessTime = _book.LastAccessTime
                })
                .OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pages.Count > 0)
            {
                pages[0].IsCoverPage = true;
                _coverKey = pages[0].Path;
            }
            
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
        finally
        {
            CloseArchive(archive, stream);
            FileIOThrottler.Release();
        }
        return pages;
    }
    private async Task EnsureCoverKeyInitialized(CancellationToken token)
    {
        await _pagesListLock.WaitAsync();
        try
        {
            if (_pagesListTask == null)
                _pagesListTask = Task.Run(()=>GetPagesListAsync(), token);

            await _pagesListTask;
        }
        finally { _pagesListLock.Release(); }
    }
    private MemoryStream? ExtractPage(IArchive archive, string key)
    {
        MemoryStream stream = null;
        //Console.WriteLine($"[{_book.Path}] Starting extraction of {page.Path}");
        try
        {
            var entry = archive.Entries.FirstOrDefault(e => e.Key == key);
            if (entry == null)
            {
                Console.WriteLine($"[{Path}] Entry not found: {key}");
                return null;
            }

            stream = new MemoryStream((int)entry.Size);
            entry.WriteTo(stream);
            stream.Seek(0, SeekOrigin.Begin);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }

        return stream;
        //Console.WriteLine($"[{_book.Path}] Completed extraction of {page.Path}");
    }
    
    private async Task DispatchThumbnail(Stream stream, Action<Bitmap> callback, CancellationToken ct)
    {
        //Console.WriteLine("Archive Loading Thumbnail image for {0}", Path);
        var thumbnail = await Task.Run(() => ThumbnailService.LoadThumbnail(stream, 240), ct);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                //Console.WriteLine("Archive returning {0} thumbnail : {1}x{2}px", Path, thumbnail.PixelSize.Width, thumbnail.PixelSize.Height);
                callback(thumbnail);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                thumbnail.Dispose(); throw;
            }
        });
    }
    private async Task LoadPageThumbnail(Action<Bitmap> callback, CancellationToken ct, string key)
    {
      
        Stream? stream = null;
        try
        {
            await FileIOThrottler.WaitAsync(ct);
            IArchive archive = null;
            FileStream fstream = null;
            try
            {
                var pair = TryOpenArchive();
                if (pair == null) throw (new FileLoadException($"Could not open archive file : {Path}"));
                (archive, fstream) = pair.Value;
                stream = ExtractPage(archive, key);
            }
            catch (TaskCanceledException){}
            catch (OperationCanceledException) { }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); }
            finally
            {
                CloseArchive(archive, fstream);
                FileIOThrottler.Release();
            }

            if (stream != null) await DispatchThumbnail(stream, callback, ct);

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Thumbnail load failed: {ex}");
            callback(null);
        }
        finally
        {
            stream?.Dispose();
        }

    }
    
    #region public_methods
    
    public override async Task LoadPagesList(Action<List<Page>> callback)
    {

        PagesListCts?.Cancel();
        PagesListCts?.Dispose();
        PagesListCts = new CancellationTokenSource();
        var token = PagesListCts.Token;

        await _pagesListLock.WaitAsync();
        try
        {
            if (_pagesListTask == null)
            {
                    token.ThrowIfCancellationRequested();
                    _pagesListTask = Task.Run(() => GetPagesListAsync());
            }

            var pages  = await _pagesListTask;

            callback(pages);
        }
        catch (TaskCanceledException){}
        catch (OperationCanceledException) { }
        catch (Exception ex) { Console.WriteLine(ex.ToString()); }
        finally { _pagesListLock.Release(); }
    }

    

    public override async Task LoadThumbnailAsync(Action<Bitmap> callback)
    {
        ThumbnailCts?.Cancel();
        ThumbnailCts?.Dispose();
        ThumbnailCts = new CancellationTokenSource();
        var token = ThumbnailCts.Token;
        
        try
        {
            await EnsureCoverKeyInitialized(token);
            await LoadPageThumbnail(callback, token, _coverKey);
        }
        catch (TaskCanceledException){}
        finally
        {
            ThumbnailCts?.Dispose();
            ThumbnailCts = null;
        }
    }

    public override async Task LoadThumbnailAsync(Action<Bitmap> callback, string key)
    {
        //can't provide key argument without having a list of pages
        //no need to ensure _pagesListTask has completed, it's a given.

        if (!PagesCts.ContainsKey(key))
            throw new ArgumentException("Invalid page path in BookArchive.LoadThumnailAsync");

        PagesCts[key]?.Cancel();
        PagesCts[key]?.Dispose();
        PagesCts[key] = new CancellationTokenSource();
        var token = PagesCts[key].Token;
        try
        {
            await LoadPageThumbnail(callback, token, key);
        }
        finally
        {
            PagesCts[key]?.Dispose();
            PagesCts[key] = null;
        }
    }

    public override async Task LoadFullImageAsync(Page page, Action<Bitmap?> callback, CancellationToken token)
    {
        var key = page.Path;
        
        Bitmap? bmp = null;
        Stream? stream = null;
        try
        {
            await FileIOThrottler.WaitAsync(token);
            IArchive archive = null;
            FileStream fstream = null;
            try
            {
                var pair = TryOpenArchive();
                if (pair == null) throw (new FileLoadException($"Could not open archive file : {Path}"));
                (archive, fstream) = pair.Value;

                stream = ExtractPage(archive, key);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                CloseArchive(archive, fstream);
                FileIOThrottler.Release();
            }

            if (stream != null)
            {
                bmp = await Task.Run(() => new Bitmap(stream), token);
                await Dispatcher.UIThread.InvokeAsync(() => callback(bmp));
            }
        }
        catch (Exception ex)
        {
            if (!(ex is OperationCanceledException))
                Console.WriteLine($"Thumbnail load failed: {ex}");
            bmp?.Dispose();
            callback(null);
        }
        finally
        {
            stream?.Dispose(); 
        }
    }

    public override string IvpPath =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? System.IO.Path.GetDirectoryName(Path)! + "\\.ivp"
            : System.IO.Path.GetDirectoryName(Path)! + "/.ivp";
    #endregion
}

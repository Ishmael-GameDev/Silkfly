namespace Silkfly.Core.Infrastructure;

public interface IShellService
{
    void OpenFolder(string path);

    void OpenUrl(Uri url);

    void OpenFile(string path);

    void RevealInFileManager(string path);
}

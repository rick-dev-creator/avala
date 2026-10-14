using System.Security.Cryptography;
using System.Text;
using Avala.Components.Code;
using Avala.Sdk;

namespace Avala.Workbench.Linking;

internal sealed class CodeFiles(IFileSaver saver, IFileOpener opener, AvalaPaths paths)
{
    public const string SaveTitle = "Save the code";

    public async Task<Result<string, CodeRefusal>> HandleAsync(CodeRequest request, CancellationToken cancellationToken) =>
        request.Action == CodeAction.Save
            ? (await saver.SaveAsync(SaveTitle, $"code{request.Extension}", request.Code, cancellationToken)).Match<Result<string, CodeRefusal>>(
                saved => saved,
                error => error switch
                {
                    FileSaveError.Cancelled => CodeRefusal.Cancelled,
                    FileSaveError.Unavailable => CodeRefusal.Unavailable,
                    _ => CodeRefusal.Unwritable,
                })
            : (await opener.OpenAsync(Scratch(request), request.Code, cancellationToken)).Match<Result<string, CodeRefusal>>(
                opened => opened.Path,
                error => error switch
                {
                    FileOpenError.Uncreatable => CodeRefusal.Unwritable,
                    FileOpenError.Unavailable => CodeRefusal.Unavailable,
                    _ => CodeRefusal.NoEditor,
                });

    public string Scratch(CodeRequest request) =>
        Path.Combine(paths.Folder("code"), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request.Code)))[..16] + request.Extension);
}

internal enum CodeRefusal
{
    Cancelled,
    Unavailable,
    Unwritable,
    NoEditor,
}

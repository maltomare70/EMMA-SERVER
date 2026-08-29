using Microsoft.AspNetCore.Http;

namespace EmmaServer.Tests.Infrastructure;

/// <summary>
/// <see cref="IFormFile"/> in memoria per i test di import.
/// Importante: <see cref="OpenReadStream"/> restituisce ogni volta uno stream NUOVO, perche'
/// DocService.ImportFatturaElettronicaAsync apre il file due volte (una per i byte, una per l'XML).
/// </summary>
public sealed class FakeFormFile : IFormFile
{
    private readonly byte[] _contenuto;

    public FakeFormFile(byte[] contenuto, string fileName, string contentType = "application/octet-stream", string name = "file")
    {
        _contenuto = contenuto;
        FileName = fileName;
        ContentType = contentType;
        Name = name;
    }

    public static FakeFormFile DaTesto(string testo, string fileName, string contentType = "text/plain")
        => new(System.Text.Encoding.UTF8.GetBytes(testo), fileName, contentType);

    public string ContentType { get; }

    public string ContentDisposition => $"form-data; name=\"{Name}\"; filename=\"{FileName}\"";

    public IHeaderDictionary Headers { get; } = new HeaderDictionary();

    public long Length => _contenuto.Length;

    public string Name { get; }

    public string FileName { get; }

    public void CopyTo(Stream target) => target.Write(_contenuto, 0, _contenuto.Length);

    public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        => target.WriteAsync(_contenuto, 0, _contenuto.Length, cancellationToken);

    public Stream OpenReadStream() => new MemoryStream(_contenuto, writable: false);
}

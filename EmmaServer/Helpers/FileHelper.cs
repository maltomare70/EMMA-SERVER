

public static class FileHelper
{
    //utility per convertire un file in byte[] per salvare su un databse
    public static async Task<byte[]> ConvertFormFileToByteArray(IFormFile file)
    {
        // 1. Controllo di sicurezza: verifichiamo che il file non sia vuoto
        if (file == null || file.Length == 0)
        {
            return Array.Empty<byte>(); // Oppure gestisci l'errore come preferisci
        }
    
        // 2. Usiamo un MemoryStream per leggere il contenuto del file
        using var memoryStream = new MemoryStream();
        
        // 3. Copiamo asincronamente lo stream del file nel MemoryStream
        await file.CopyToAsync(memoryStream);
    
        // 4. Trasformiamo il MemoryStream in un array di byte
        return memoryStream.ToArray();
    }
}
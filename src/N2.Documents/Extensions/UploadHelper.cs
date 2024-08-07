namespace N2.Documents.Extensions;

public static class UploadHelper
{
    private static readonly List<string> audioExtensions = [".MP3", ".WAV"];
    private static readonly List<string> csvExtensions = [".CSV"];
    private static readonly List<string> documentExtensions = [".DOC", ".DOCX", ".ODT"];
    private static readonly List<string> excelExtensions = [".XLS", ".XLSX"];
    private static readonly List<string> imageExtensions = [".BMP", ".GIF", ".JPEG", ".JPG", ".PNG", ".TIF", ".TIFF"];
    private static readonly List<string> movieExtensions = [".AVI", ".ASF", ".MPG", ".MPEG", ".MOV", ".MP4", ".MKV", ".3GP", ".WMV", ".WEBM", ".OGG"];
    private static readonly List<string> pdfExtensions = [".PDF"];
    private static readonly List<string> powerpointExtensions = [".PPT", ".PPS", ".PPTX", ".PPSX"];
    private static readonly List<string> textExtensions = [".TXT"];

    public static string ExtensionType(string extensionArg)
    {
        if (string.IsNullOrEmpty(extensionArg))
        {
            return string.Empty;
        }

        var extension = extensionArg.ToUpperInvariant();

        if (imageExtensions.Contains(extension))
        {
            return FileExtensionType.Image;
        }

        if (movieExtensions.Contains(extension))
        {
            return FileExtensionType.Movie;
        }

        if (excelExtensions.Contains(extension))
        {
            return FileExtensionType.Excel;
        }

        if (powerpointExtensions.Contains(extension))
        {
            return FileExtensionType.Powerpoint;
        }

        if (textExtensions.Contains(extension))
        {
            return FileExtensionType.Text;
        }

        if (documentExtensions.Contains(extension))
        {
            return FileExtensionType.Document;
        }

        if (pdfExtensions.Contains(extension))
        {
            return FileExtensionType.Pdf;
        }

        if (csvExtensions.Contains(extension))
        {
            return FileExtensionType.Csv;
        }

        if (audioExtensions.Contains(extension))
        {
            return FileExtensionType.Audio;
        }

        return string.Empty;
    }

    public static bool IsValidExtension(string filename)
    {
        var fi = new FileInfo(filename);
        var ext = fi.Extension.ToUpperInvariant();

        if (imageExtensions.Contains(ext))
        {
            return true;
        }

        if (movieExtensions.Contains(ext))
        {
            return true;
        }

        if (excelExtensions.Contains(ext))
        {
            return true;
        }

        if (powerpointExtensions.Contains(ext))
        {
            return true;
        }

        if (textExtensions.Contains(ext))
        {
            return true;
        }

        if (documentExtensions.Contains(ext))
        {
            return true;
        }

        if (pdfExtensions.Contains(ext))
        {
            return true;
        }

        if (csvExtensions.Contains(ext))
        {
            return true;
        }

        if (audioExtensions.Contains(ext))
        {
            return true;
        }

        return false;
    }
}
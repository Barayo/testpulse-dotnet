using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TestPulse.Internal;

public sealed record StoredAttachment(string CaseKey, string Filename, string ContentType, byte[] Data);

public static class AttachmentReader
{
    public static List<StoredAttachment> ReadAll(string scratchDir)
    {
        var result = new List<StoredAttachment>();
        if (!Directory.Exists(scratchDir))
        {
            return result;
        }

        foreach (var jsonPath in Directory.GetFiles(scratchDir, "*.json"))
        {
            var meta = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var caseKey = meta.RootElement.GetProperty("caseKey").GetString()!;
            var filename = meta.RootElement.GetProperty("filename").GetString()!;
            var contentType = meta.RootElement.GetProperty("contentType").GetString()!;

            var dataPath = Path.ChangeExtension(jsonPath, ".data");
            var data = File.ReadAllBytes(dataPath);

            result.Add(new StoredAttachment(caseKey, filename, contentType, data));
        }

        return result;
    }
}

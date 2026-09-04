using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Xunit.v3;

namespace TestPulse;

public static class TestPulseAttachments
{
    private static readonly string[] AllowedContentTypes = { "image/png", "image/jpeg", "image/webp" };

    /// <summary>
    /// Records a screenshot/artifact attachment for caseKey, which must
    /// equal the currently-executing test method's own [TestPulseCase]
    /// case key -- identified via xUnit v3's TestContext.Current, not
    /// stack-frame inference or a hand-rolled ambient value.
    /// </summary>
    public static void Attach(string caseKey, byte[] data, string filename, string contentType)
    {
        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new ArgumentException(
                $"testpulse: unsupported content type '{contentType}' (allowed: {string.Join(", ", AllowedContentTypes)})",
                nameof(contentType));
        }

        var testMethod = TestContext.Current.TestMethod as IXunitTestMethod;
        if (testMethod is null)
        {
            throw new InvalidOperationException(
                "testpulse: Attach called outside an active TestPulse test execution (TestContext.Current.TestMethod was null -- e.g. called from constructor/fixture setup, or from an un-awaited background task).");
        }

        var declared = testMethod.Method.GetCustomAttribute<TestPulseCaseAttribute>();
        if (declared is null || declared.CaseKey != caseKey)
        {
            throw new InvalidOperationException(
                $"testpulse: case key '{caseKey}' was not declared via [TestPulseCase] on the currently-executing test method '{testMethod.Method.Name}'.");
        }

        WriteAttachment(caseKey, data, filename, contentType);
    }

    private static void WriteAttachment(string caseKey, byte[] data, string filename, string contentType)
    {
        var dir = ScratchDir();
        Directory.CreateDirectory(dir);

        var id = Guid.NewGuid().ToString("N");
        var hash = HashInvocation(caseKey, id);

        File.WriteAllBytes(Path.Combine(dir, hash + ".data"), data);
        File.WriteAllText(Path.Combine(dir, hash + ".json"), SerializeMeta(caseKey, filename, contentType));
    }

    private static string SerializeMeta(string caseKey, string filename, string contentType)
    {
        // Deliberately simple, hand-written JSON (three known-safe-ish
        // string fields) rather than pulling in a JSON library dependency
        // just for this -- values are escaped for the characters that
        // matter in a JSON string.
        return "{"
            + $"\"caseKey\":{JsonString(caseKey)},"
            + $"\"filename\":{JsonString(filename)},"
            + $"\"contentType\":{JsonString(contentType)}"
            + "}";
    }

    private static string JsonString(string value)
    {
        var sb = new StringBuilder();
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string HashInvocation(string caseKey, string id)
    {
        var bytes = Encoding.UTF8.GetBytes(caseKey + ":" + id);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static string ScratchDir()
    {
        // AppContext.BaseDirectory (the running assembly's own directory,
        // i.e. the build output dir) is used rather than
        // Directory.GetCurrentDirectory(), since the test host's working
        // directory was found empirically to differ depending on how
        // `dotnet test` launches it (legacy VSTest adapter vs. the newer
        // Microsoft.Testing.Platform mode) -- AppContext.BaseDirectory is
        // reliable across both.
        return Path.Combine(AppContext.BaseDirectory, ".testpulse", "attachments");
    }
}

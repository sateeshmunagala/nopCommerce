using System.IO;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

internal static class TestFilePathHelper
{
    internal static string GetPluginRootPath()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory != null)
        {
            var pluginRoot = Path.Combine(directory.FullName, "src", "Plugins", "Nop.Plugin.Misc.AIInterview");
            if (Directory.Exists(pluginRoot))
                return Path.GetFullPath(pluginRoot);

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the AIInterview plugin root from the current test directory.");
    }

    internal static string GetPluginFilePath(params string[] relativePathParts)
    {
        return Path.GetFullPath(Path.Combine(GetPluginRootPath(), Path.Combine(relativePathParts)));
    }
}

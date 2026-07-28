using TypelessSwitch.Core;

namespace TypelessSwitch.Tests;

public sealed class TypelessPathsTests
{
    [Fact]
    public void DefaultExportPaths_AreDerivedFromDocumentsDirectory()
    {
        var documents = Path.Combine("test-root", "Documents");
        var paths = new TypelessPaths(documentsDirectory: documents);

        Assert.Equal(Path.Combine(documents, "Typeless Switch", "Exports"), paths.DefaultExportDirectory);
        Assert.Equal(
            Path.Combine(documents, "Typeless Switch", "Exports", "typeless-dictionary-export.json"),
            paths.DefaultExportJsonFile);
    }
}

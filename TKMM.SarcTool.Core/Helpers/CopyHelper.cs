namespace TKMM.SarcTool.Core;

internal static class CopyHelper {
    public static void CopyFile(string source, string destination) {
        File.Copy(source, destination, true);

        var currentAttributes = File.GetAttributes(destination);

        if (currentAttributes.HasFlag(FileAttributes.ReadOnly))
            File.SetAttributes(destination, currentAttributes ^ FileAttributes.ReadOnly);
    }
}
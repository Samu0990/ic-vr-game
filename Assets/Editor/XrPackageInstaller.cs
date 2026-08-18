using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

public static class XrPackageInstaller
{
    private static readonly string[] Packages =
    {
        "com.unity.xr.openxr",
        "com.unity.xr.interaction.toolkit",
        "com.unity.inputsystem",
        "com.unity.xr.management",
    };

    public static void InstallAll()
    {
        InstallNext(0);
    }

    private static void InstallNext(int index)
    {
        if (index >= Packages.Length)
        {
            Debug.Log("[XrPackageInstaller] DONE");
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
            return;
        }

        string pkg = Packages[index];
        Debug.Log("[XrPackageInstaller] Adding " + pkg);
        var request = Client.Add(pkg);

        EditorApplication.CallbackFunction poll = null;
        poll = () =>
        {
            if (!request.IsCompleted) return;
            EditorApplication.update -= poll;

            if (request.Status == StatusCode.Success)
                Debug.Log("[XrPackageInstaller] SUCCESS " + pkg + " -> " + request.Result.version);
            else
                Debug.LogError("[XrPackageInstaller] FAILED " + pkg + " -> " + request.Error.message);

            InstallNext(index + 1);
        };
        EditorApplication.update += poll;
    }
}

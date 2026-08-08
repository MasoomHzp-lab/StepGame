#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Normalizes Root Transform import settings for the StepGame clips.
/// Run manually from Tools > Step Game > Fix Animation Import Settings.
/// A copy of each .meta file is saved outside Assets before changes are applied.
/// </summary>
public static class StairAnimationImportFixer
{
    private const string StepAssetPath = "Assets/Animation/step.fbx";
    private const string IdleAssetPath = "Assets/Animation/Idle (1).fbx";

    [MenuItem("Tools/Step Game/Fix Animation Import Settings")]
    public static void FixAnimationImportSettings()
    {
        if (!EditorUtility.DisplayDialog(
                "StepGame Animation Fix",
                "This will normalize Root Transform settings for step.fbx and Idle (1).fbx. " +
                "A backup of both .meta files will be created outside Assets first.",
                "Create Backup and Fix",
                "Cancel"))
        {
            return;
        }

        try
        {
            BackupMetaFile(StepAssetPath);
            BackupMetaFile(IdleAssetPath);

            bool stepFixed = ConfigureStepClips();
            bool idleFixed = ConfigureIdleClip();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!stepFixed || !idleFixed)
            {
                EditorUtility.DisplayDialog(
                    "StepGame Animation Fix",
                    "The operation finished, but one or more animation assets or clips were not found. " +
                    "Check the Console for details.",
                    "OK"
                );
                return;
            }

            EditorUtility.DisplayDialog(
                "StepGame Animation Fix",
                "Animation import settings were normalized successfully. " +
                "Backups are stored in the StepGame_Backups folder beside Assets.",
                "OK"
            );
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "StepGame Animation Fix Failed",
                "No further changes were attempted. Check the Console for the complete error.",
                "OK"
            );
        }
    }

    private static bool ConfigureStepClips()
    {
        ModelImporter importer = AssetImporter.GetAtPath(StepAssetPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"StepGame: ModelImporter not found at '{StepAssetPath}'.");
            return false;
        }

        ModelImporterClipAnimation[] clips = GetEditableClips(importer);
        bool foundLead = false;
        bool foundJoin = false;

        foreach (ModelImporterClipAnimation clip in clips)
        {
            if (clip.name == "LeftLeadStep")
            {
                foundLead = true;
                ConfigureInPlaceClip(clip, false);
            }
            else if (clip.name == "RightJoinStep")
            {
                foundJoin = true;
                ConfigureInPlaceClip(clip, false);
            }
        }

        if (!foundLead || !foundJoin)
        {
            Debug.LogError(
                $"StepGame: Required clips were not found in '{StepAssetPath}'. " +
                $"LeftLeadStep={foundLead}, RightJoinStep={foundJoin}."
            );
            return false;
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        return true;
    }

    private static bool ConfigureIdleClip()
    {
        ModelImporter importer = AssetImporter.GetAtPath(IdleAssetPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"StepGame: ModelImporter not found at '{IdleAssetPath}'.");
            return false;
        }

        ModelImporterClipAnimation[] clips = GetEditableClips(importer);
        if (clips.Length == 0)
        {
            Debug.LogError($"StepGame: No animation clips were found in '{IdleAssetPath}'.");
            return false;
        }

        foreach (ModelImporterClipAnimation clip in clips)
        {
            ConfigureInPlaceClip(clip, true);
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        return true;
    }

    private static ModelImporterClipAnimation[] GetEditableClips(ModelImporter importer)
    {
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        return clips != null && clips.Length > 0
            ? clips
            : importer.defaultClipAnimations;
    }

    private static void ConfigureInPlaceClip(ModelImporterClipAnimation clip, bool loop)
    {
        // Root Transform Rotation: Bake Into Pose, Based Upon Body Orientation.
        clip.lockRootRotation = true;
        clip.keepOriginalOrientation = false;
        clip.rotationOffset = 0f;

        // Root Transform Position Y: Bake Into Pose, Based Upon Original.
        clip.lockRootHeightY = true;
        clip.keepOriginalPositionY = true;
        clip.heightFromFeet = false;
        clip.heightOffset = 0f;

        // Root Transform Position XZ: Bake Into Pose, Based Upon Original.
        clip.lockRootPositionXZ = true;
        clip.keepOriginalPositionXZ = true;

        clip.loopTime = loop;
        clip.loopPose = loop;
    }

    private static void BackupMetaFile(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new InvalidOperationException("Project root could not be resolved.");
        }

        string sourceMetaPath = Path.Combine(projectRoot, assetPath + ".meta");
        if (!File.Exists(sourceMetaPath))
        {
            Debug.LogWarning($"StepGame: Meta file not found for backup: '{sourceMetaPath}'.");
            return;
        }

        string backupDirectory = Path.Combine(projectRoot, "StepGame_Backups");
        Directory.CreateDirectory(backupDirectory);

        string safeName = Path.GetFileName(assetPath).Replace(' ', '_');
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string destination = Path.Combine(
            backupDirectory,
            $"{safeName}.{timestamp}.meta.backup"
        );

        File.Copy(sourceMetaPath, destination, true);
        Debug.Log($"StepGame: Backup created at '{destination}'.");
    }
}
#endif
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class StairJoinUpperBodyLayerInstaller
{
    private const string ControllerPath = "Assets/Animation/PlayerAnimator.controller";
    private const string StepFbxPath = "Assets/Animation/step.fbx";
    private const string MaskPath = "Assets/Animation/StairJoinUpperBody.mask";
    private const string LayerName = "Join Upper Body";
    private const string LeftStateName = "JoinUpperBody_Left";
    private const string RightStateName = "JoinUpperBody_Right";

    [MenuItem("Tools/Stair Game/Install Join Upper Body Layer")]
    public static void Install()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"Stair Game: Animator Controller not found at {ControllerPath}");
            return;
        }

        AnimationClip leadClip = AssetDatabase.LoadAllAssetsAtPath(StepFbxPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == "LeftLeadStep");

        if (leadClip == null)
        {
            Debug.LogError($"Stair Game: LeftLeadStep clip not found inside {StepFbxPath}");
            return;
        }

        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null)
        {
            mask = new AvatarMask { name = "StairJoinUpperBody" };
            ConfigureUpperBodyMask(mask);
            AssetDatabase.CreateAsset(mask, MaskPath);
        }
        else
        {
            ConfigureUpperBodyMask(mask);
            EditorUtility.SetDirty(mask);
        }

        int existingIndex = System.Array.FindIndex(controller.layers, l => l.name == LayerName);
        AnimatorControllerLayer layer;

        if (existingIndex >= 0)
        {
            layer = controller.layers[existingIndex];
            layer.avatarMask = mask;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layer.defaultWeight = 0f;

            EnsureStates(layer.stateMachine, leadClip);

            AnimatorControllerLayer[] layers = controller.layers;
            layers[existingIndex] = layer;
            controller.layers = layers;
        }
        else
        {
            AnimatorStateMachine stateMachine = new AnimatorStateMachine
            {
                name = LayerName
            };
            AssetDatabase.AddObjectToAsset(stateMachine, controller);

            EnsureStates(stateMachine, leadClip);

            layer = new AnimatorControllerLayer
            {
                name = LayerName,
                avatarMask = mask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                defaultWeight = 0f,
                stateMachine = stateMachine,
                syncedLayerIndex = -1,
                iKPass = false
            };

            AnimatorControllerLayer[] oldLayers = controller.layers;
            AnimatorControllerLayer[] newLayers = new AnimatorControllerLayer[oldLayers.Length + 1];
            for (int i = 0; i < oldLayers.Length; i++)
            {
                newLayers[i] = oldLayers[i];
            }
            newLayers[newLayers.Length - 1] = layer;
            controller.layers = newLayers;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Stair Game: Join Upper Body layer installed. It uses LeftLeadStep for the torso only; both legs remain on the Base Layer.");
    }

    private static void EnsureStates(AnimatorStateMachine stateMachine, AnimationClip leadClip)
    {
        AnimatorState left = stateMachine.states
            .Select(s => s.state)
            .FirstOrDefault(s => s != null && s.name == LeftStateName);
        if (left == null)
        {
            left = stateMachine.AddState(LeftStateName);
        }
        left.motion = leadClip;
        left.mirror = false;
        left.speed = 1f;
        left.writeDefaultValues = true;

        AnimatorState right = stateMachine.states
            .Select(s => s.state)
            .FirstOrDefault(s => s != null && s.name == RightStateName);
        if (right == null)
        {
            right = stateMachine.AddState(RightStateName);
        }
        right.motion = leadClip;
        right.mirror = true;
        right.speed = 1f;
        right.writeDefaultValues = true;

        stateMachine.defaultState = left;
    }

    private static void ConfigureUpperBodyMask(AvatarMask mask)
    {
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, false);
    }
}
#endif

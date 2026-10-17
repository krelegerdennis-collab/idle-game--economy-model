using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Playtest.Editor
{
    public static class ApplyAppIcon
    {
        const string IconPath = "Assets/Icons/AppIcon.png";

        [MenuItem("Tools/Apply App Icon")]
        public static void Apply()
        {
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogError("Missing application icon at " + IconPath);
                return;
            }

            Texture2D[] icons = { icon };
#pragma warning disable 618
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, icons);
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Android, icons);
#pragma warning restore 618
            ApplyAndroidPlatformIcons(icon);
            AssetDatabase.SaveAssets();
            Debug.Log("Applied " + IconPath + " as the default and Android application icon.");
        }

        // AndroidPlatformIconKind lives in the Android editor module. The WebGL
        // editor image does not ship that module, so this stays on reflection.
        static void ApplyAndroidPlatformIcons(Texture2D icon)
        {
            Type kindType = FindAndroidIconKind();
            if (kindType == null)
            {
                Debug.Log("Android icon kinds are not loaded. Legacy icons were set. Open the project with the Android module and run Tools/Apply App Icon again for adaptive icons.");
                return;
            }

            MethodInfo getIcons = FindPlatformIconMethod("GetPlatformIcons", 2);
            MethodInfo setIcons = FindPlatformIconMethod("SetPlatformIcons", 3);
            if (getIcons == null || setIcons == null)
            {
                Debug.LogWarning("PlayerSettings platform icon API was not found.");
                return;
            }

            object android = NamedBuildTarget.Android;
            string[] kindNames = { "Legacy", "Round", "Adaptive" };
            foreach (string kindName in kindNames)
            {
                FieldInfo field = kindType.GetField(kindName, BindingFlags.Public | BindingFlags.Static);
                if (field == null)
                    continue;
                object kind = field.GetValue(null);
                Array platformIcons = getIcons.Invoke(null, new object[] { android, kind }) as Array;
                if (platformIcons == null)
                    continue;
                foreach (object platformIcon in platformIcons)
                {
                    MethodInfo setTextures = platformIcon.GetType().GetMethod("SetTextures", new[] { typeof(Texture2D[]) });
                    if (setTextures == null)
                        continue;
                    Texture2D[] layers = new Texture2D[] { icon };
                    if (kindName == "Adaptive")
                        layers = new Texture2D[] { icon, icon };
                    setTextures.Invoke(platformIcon, new object[] { layers });
                }
                setIcons.Invoke(null, new object[] { android, kind, platformIcons });
            }
        }

        static Type FindAndroidIconKind()
        {
            const string typeName = "UnityEditor.Android.AndroidPlatformIconKind";
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = assembly.GetType(typeName);
                if (found != null)
                    return found;
            }
            try
            {
                Assembly loaded = Assembly.Load("UnityEditor.Android.Extensions");
                return loaded == null ? null : loaded.GetType(typeName);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static MethodInfo FindPlatformIconMethod(string name, int parameterCount)
        {
            foreach (MethodInfo method in typeof(PlayerSettings).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != name)
                    continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == parameterCount && parameters[0].ParameterType == typeof(NamedBuildTarget))
                    return method;
            }
            return null;
        }
    }
}

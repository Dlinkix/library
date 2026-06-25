using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FightMapSceneBuilder
{
    private const string ConfigPath = "Assets/Data/FightMapConfig.asset";
    private const string MapRootName = "FightMapRoot";

    [MenuItem("Tools/Fight/Build Fight Map In Scene")]
    public static void BuildFightMapInScene()
    {
        FightMapConfig config = AssetDatabase.LoadAssetAtPath<FightMapConfig>(ConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<FightMapConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
        }

        GameObject sceneRoad = GameObject.Find("Canvas")?.transform.Find("bg/SceneRoad")?.gameObject;
        if (sceneRoad == null) return;

        FightMapGenerator generator = sceneRoad.GetComponent<FightMapGenerator>();
        if (generator == null)
            generator = sceneRoad.AddComponent<FightMapGenerator>();

        RectTransform mapRoot = sceneRoad.transform.Find(MapRootName) as RectTransform;
        if (mapRoot == null)
        {
            GameObject rootObject = new GameObject(MapRootName, typeof(RectTransform));
            mapRoot = rootObject.GetComponent<RectTransform>();
            mapRoot.SetParent(sceneRoad.transform, false);
            mapRoot.anchorMin = Vector2.zero;
            mapRoot.anchorMax = Vector2.one;
            mapRoot.offsetMin = Vector2.zero;
            mapRoot.offsetMax = Vector2.zero;
            mapRoot.anchoredPosition = Vector2.zero;
            mapRoot.localScale = Vector3.one;
        }

        mapRoot.gameObject.layer = sceneRoad.layer;

        SerializedObject generatorSO = new SerializedObject(generator);
        generatorSO.FindProperty("config").objectReferenceValue = config;
        generatorSO.FindProperty("mapRoot").objectReferenceValue = mapRoot;
        generatorSO.ApplyModifiedPropertiesWithoutUndo();

        generator.BuildMap();
        mapRoot.gameObject.SetActive(false);

        EditorUtility.SetDirty(config);
        EditorUtility.SetDirty(generator);
        EditorUtility.SetDirty(mapRoot.gameObject);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveOpenScenes();
    }
}
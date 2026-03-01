#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "BehaviorTree/Graph", fileName = "NewBehaviorTree")]
public class BehaviorTreeGraph : ScriptableObject
{
    public RootNode root;
    [HideInInspector]
    public List<AbstractNode> nodes;
    
    [HideInInspector]
    public List<NodeConnection> connections = new List<NodeConnection>();
    
    [HideInInspector]
    public string boundContextTypeName;
    
#if UNITY_EDITOR
    private void OnEnable()
    {
        if (nodes == null)
            nodes = new List<AbstractNode>();
        if (root == null)
        {
            root = ScriptableObject.CreateInstance<RootNode>();
            root.name = "Root";
            root.children.Add(null);
            AssetDatabase.AddObjectToAsset(root, this);
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(this);
        }
    }
#endif
}

[System.Serializable]
public class NodeConnection
{
    public string fromNodeID;
    public int fromPortIndex;

    public string toNodeID;
    public int toPortIndex;
}
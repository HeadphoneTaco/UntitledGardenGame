using CoreUtils.AssetBuckets;
using UnityEngine;

namespace RevManager {
    /// <summary>
    /// Auto-collects every ActionData asset under its source folder.
    /// Add a new action asset to the folder and it shows up in game. No scene wiring.
    /// </summary>
    [CreateAssetMenu(menuName = "RevManager/Buckets/Action Bucket", fileName = "ActionBucket")]
    public class ActionBucket : GenericAssetBucket<ActionData> { }
}

using CoreUtils.AssetBuckets;
using UnityEngine;

namespace RevManager {
    /// <summary>
    /// Auto-collects ResourceData assets under its source folder(s).
    /// Two instances drive the UI:
    ///   InventoryBucket      - source: ScriptableObjects/Resources/  (whole grid)
    ///   DrainResourceBucket  - source: ScriptableObjects/Resources/Draining/
    /// Folder search is recursive, so Food and Water live in Draining/ and
    /// appear in both. Drop a new resource asset in the right folder and it
    /// shows up in game. No scene wiring.
    /// </summary>
    [CreateAssetMenu(menuName = "RevManager/Buckets/Resource Bucket", fileName = "ResourceBucket")]
    public class ResourceBucket : GenericAssetBucket<ResourceData> { }
}

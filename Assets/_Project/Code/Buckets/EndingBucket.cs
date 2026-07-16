using CoreUtils.AssetBuckets;
using UnityEngine;

namespace RevManager {
    /// <summary>
    /// Auto-collects every EndingData asset under its source folder.
    /// </summary>
    [CreateAssetMenu(menuName = "RevManager/Buckets/Ending Bucket", fileName = "EndingBucket")]
    public class EndingBucket : GenericAssetBucket<EndingData> { }
}

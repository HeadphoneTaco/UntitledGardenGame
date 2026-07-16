using CoreUtils.AssetBuckets;
using UnityEngine;

namespace RevManager {
    /// <summary>
    /// Auto-collects every NewsEventData asset under its source folder.
    /// </summary>
    [CreateAssetMenu(menuName = "RevManager/Buckets/News Event Bucket", fileName = "NewsEventBucket")]
    public class NewsEventBucket : GenericAssetBucket<NewsEventData> { }
}

using CoreUtils.AssetBuckets;
using UnityEngine;

namespace RevManager {
    /// <summary>
    /// Auto-collects the weekend choices (rest / small action / big mobilization).
    /// </summary>
    [CreateAssetMenu(menuName = "RevManager/Buckets/Weekend Option Bucket", fileName = "WeekendOptionBucket")]
    public class WeekendOptionBucket : GenericAssetBucket<WeekendOptionData> { }
}

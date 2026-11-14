using UnityEngine;

namespace SoloBuffs
{
    public class SoloBuffsBehaviour : MonoBehaviour
    {
        private float _nextScan = 0f;
        private const float PeriodicInterval = 10f;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            float now = Time.time;

            // Run on timer expiry (offline threshold) or periodic scan
            if (now >= _nextScan || SoloBuffsLogic.ConsumePendingRefresh())
            {
                SoloBuffsLogic.UpdateSoloBuffs();
                ScheduleNext();
            }
        }

        private void ScheduleNext()
        {
            float now = Time.time;
            float secondsUntilExpiry = SoloBuffsLogic.GetSecondsUntilNextExpiry();
            float nextExpiry = secondsUntilExpiry < float.MaxValue
                ? now + secondsUntilExpiry
                : float.MaxValue;

            // Wake up at whichever comes first: timer expiry or periodic scan
            _nextScan = Mathf.Min(nextExpiry, now + PeriodicInterval);
        }
    }
}
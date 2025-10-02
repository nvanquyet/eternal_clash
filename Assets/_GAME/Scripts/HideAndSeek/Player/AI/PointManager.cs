using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;

namespace _GAME.Scripts.HideAndSeek.Player.AI
{
    public class PointManager : GAME.Scripts.DesignPattern.Singleton<PointManager>
    {
        [Header("=== POI Database ===")]
        [SerializeField] private List<Citizen_AIScript.PointOfInterest> allPOIs = new List<Citizen_AIScript.PointOfInterest>();

        [Header("=== Auto-Setup ===")]
        [SerializeField] private bool autoFindPoints = true;
        [SerializeField] private string pointNamePrefix = "Point_";
        [SerializeField] private GameObject[] allPoint;
        
        [ContextMenu("Auto find points in children")]
        private void FetchAllPoint()
        {
            List<GameObject> pointObjects = new();
            foreach(Transform t in transform)
            {
                if(t.gameObject.name.StartsWith(pointNamePrefix)) pointObjects.Add(t.gameObject);
            }
            allPoint = pointObjects.ToArray();
        }
        
        private void Start()
        {
            if (autoFindPoints)
            {
                AutoSetupPoints();
            }
        }

        private void AutoSetupPoints()
        {
            // Find all GameObjects with POI tags
           
            
            foreach (var obj in allPoint)
            {
                // Check if already in list
                if (allPOIs.Any(p => p.location == obj.transform))
                    continue;

                // Try to determine type from name
                Citizen_AIScript.PointType type = Citizen_AIScript.PointType.RandomWalk;
                string name = obj.name.ToLower();

                if (name.Contains("bench")) type = Citizen_AIScript.PointType.Bench;
                else if (name.Contains("flower") || name.Contains("plant")) type = Citizen_AIScript.PointType.FlowerPot;
                else if (name.Contains("shop") || name.Contains("counter")) type = Citizen_AIScript.PointType.ShopCounter;
                else if (name.Contains("social") || name.Contains("meet")) type = Citizen_AIScript.PointType.SocialSpot;
                else if (name.Contains("phone")) type = Citizen_AIScript.PointType.PhoneBooth;
                else if (name.Contains("wave")) type = Citizen_AIScript.PointType.WavingPoint;

                var poi = new Citizen_AIScript.PointOfInterest
                {
                    location = obj.transform,
                    type = type,
                    interactionRadius = 2f,
                    attractionWeight = 5f,
                    maxOccupants = type == Citizen_AIScript.PointType.SocialSpot ? 3 : 1
                };

                allPOIs.Add(poi);
                Debug.Log($"[POIManager] Auto-added {obj.name} as {type}");
            }
        }

        public List<Citizen_AIScript.PointOfInterest> GetAvailablePOIs(Vector3 fromPosition, float maxDistance)
        {
            return allPOIs.Where(poi => 
                poi.IsAvailable && 
                Vector3.Distance(fromPosition, poi.location.position) <= maxDistance
            ).ToList();
        }

        public void RegisterPOI(Citizen_AIScript.PointOfInterest poi)
        {
            if (!allPOIs.Contains(poi))
                allPOIs.Add(poi);
        }

        public void UnregisterPOI(Citizen_AIScript.PointOfInterest poi)
        {
            allPOIs.Remove(poi);
        }

        private void OnDrawGizmos()
        {
            if (allPOIs == null) return;

            foreach (var poi in allPOIs)
            {
                if (poi.location == null) continue;

                Color color = poi.IsAvailable ? Color.cyan : Color.red;
                color.a = 0.3f;
                Gizmos.color = color;
                Gizmos.DrawWireSphere(poi.location.position, poi.interactionRadius);

                // Draw type icon
                #if UNITY_EDITOR
                UnityEditor.Handles.Label(poi.location.position + Vector3.up * 2f, 
                    $"{poi.type}\n{poi.currentOccupants}/{poi.maxOccupants}");
                #endif
            }
        }
    }
}

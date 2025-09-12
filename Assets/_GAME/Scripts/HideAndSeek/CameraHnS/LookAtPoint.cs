using System;
using _GAME.Scripts.HideAndSeek.Camera;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations;

namespace _GAME.Scripts.HideAndSeek.CameraHnS
{
    public class LookAtPoint : MonoBehaviour
    {
       [SerializeField] private LookAtConstraint lookAtConstraint;

       public void Init()
       {
           var source = new ConstraintSource
           {
               sourceTransform = CameraCustom.Instance.AimTarget,
               weight = 1f
           };
           lookAtConstraint.SetSource(0, source);
       }
    }
}

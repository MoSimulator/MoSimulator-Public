// using UnityEngine;
//
// namespace Robots.Climbing
// {
//     public class GroundChecker : MonoBehaviour
//     {
//     private OldClimbScorer _scorer;
//
//         private void Start()
//         {
//             _scorer = GetComponentInParent<OldClimbScorer>();
//             if (_scorer == null)
//             {
//                 Debug.LogError("GroundChecker: ClimbScorer component not found in parent.");
//             }
//         }
//
//         private void OnTriggerEnter(Collider other)
//         {
//             if (other.CompareTag("Carpet"))
//             {
//                 _scorer?.AddTouchingGround();
//             }
//         }
//
//         private void OnTriggerExit(Collider other)
//         {
//             if (other.CompareTag("Carpet"))
//             {
//                 _scorer?.RemoveTouchingGround();
//             }
//         }
//     }
// }

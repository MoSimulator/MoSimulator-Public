using UnityEngine;

namespace FieldScripts
{
    public class GamePieceScript : MonoBehaviour
    {
        private Rigidbody _rb;
    
        public bool IsScored { get; set; } = false;
    
        // Start is called before the first frame update
        void Start()
        {
            _rb = GetComponent<Rigidbody>();

            _rb.solverIterations = 1;
            _rb.solverVelocityIterations = 1;
        }

        // Update is called once per frame
        void Update()
        {
        
        }
    }
}

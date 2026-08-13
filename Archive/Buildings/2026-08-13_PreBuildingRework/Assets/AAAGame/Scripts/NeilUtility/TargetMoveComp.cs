using UnityEngine;
using BaseUtility;

namespace BulletToolKit
{
    public struct TargetMoveCompInitData
    {
        public Transform target;
        public float speed;
        public float lifeTime;
        public bool ifTwoD;

        public TargetMoveCompInitData(Transform target, float speed, float lifeTime, bool ifTwoD)
        {
            this.target = target;
            this.speed = speed;
            this.lifeTime = lifeTime;
            this.ifTwoD = ifTwoD;
        }
    }

    public class TargetMoveComp : MonoBehaviour, IMoveCompo
    {
        bool canMove = true;
        protected Transform target;
        protected float speed;
        protected float lifeTime;
        protected bool ifTwoD;

        public void AllowMove()
        {
            canMove = true;
        }
        public void BanMove()
        {
            canMove = false;
        }

        public void Init(Transform target, float speed, float lifeTime, bool ifTwoD)
        {
            this.target = target;
            this.speed = speed;
            this.lifeTime = lifeTime;
            this.ifTwoD = ifTwoD;
        }

        void Update()
        {
            if(canMove && target != null)
            {
                Vector3 newPosition = Vector3.MoveTowards(transform.position, target.position, Time.deltaTime * speed);
                if (ifTwoD)
                {
                    newPosition.z = transform.position.z;
                }

                Vector3 moveDir = newPosition - transform.position;
                if(moveDir.magnitude >0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(moveDir);
                }
                transform.position = newPosition;
                lifeTime -= Time.deltaTime;
                if (lifeTime <= 0f)
                {
                    Destroy(this.gameObject);
                }
            }
        }
    }
}

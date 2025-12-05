using UnityEngine;
using BaseUtility;

namespace BulletToolKit
{

    public struct DirectionMoveCompoInitData
    {
        public Vector3 dir;
        public float speed;
        public float lifeTime;

        public DirectionMoveCompoInitData(Vector3 dir, float speed, float lifeTime)
        {
            this.dir = dir;
            this.speed = speed;
            this.lifeTime = lifeTime;
        }
    }
    public class DirectionMoveCompo : MonoBehaviour, IMoveCompo
    {
        bool canMove = true;
        protected float speed;
        protected float lifeTime;
        protected Vector3 moveDir;

        public void AllowMove()
        {
            canMove = true;
        }
        public void BanMove()
        {
            canMove = false;
        }

        public void Init(Vector3 dir, float speed, float lifeTime)
        {
            this.moveDir = dir.normalized;
            this.speed = speed;
            this.lifeTime = lifeTime;
        }

        void Update()
        {
            if (canMove)
            {
                transform.position += moveDir * speed * Time.deltaTime;
                transform.rotation = Quaternion.LookRotation(moveDir)*Quaternion.Euler(0, -90, 0);
                lifeTime -= Time.deltaTime;
                if (lifeTime <= 0f)
                {
                    Destroy(this.gameObject);
                }
            }
        }
    }   
}

﻿//#define ADB_DEBUG

using UnityEngine;
using UnityEngine.Jobs;
using Unity.Jobs;
using Unity.Jobs.LowLevel;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Mathematics;

namespace ADBRuntime.Internal
{
    public unsafe class ADBRunTimeJobsTable
    {
        #region Jobs
        /// <summary>
        /// 初始化所有点的位置
        /// </summary>
        [BurstCompile]
        public struct InitiralizePoint1 : IJobParallelForTransform //OYM:先更新fixed节点

        {
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            public PointRead* pReadPoints;
            [NativeDisableUnsafePtrRestriction]
            public PointReadWrite* pReadWritePoints;

            public void Execute(int index, TransformAccess transform)
            {
                var pReadWritePoint = pReadWritePoints + index;
                var pReadPoint = pReadPoints + index;

                if (pReadPoint->parentIndex == -1)
                {
                    //Debug.Log(pReadPoint->localRotation==quaternion.Inverse(transform.localRotation));这里没问题
                    transform.localRotation = pReadPoint->initialLocalRotation;//OYM：这里改变之后,rotation也会改变

                    pReadWritePoint->rotationTemp = (transform.rotation * math.inverse(transform.localRotation));
                    //pReadWritePoint->rotationY = pReadPoint->initialRotation;
                    //Debug.Log(pReadWritePoint->rotation+" "+index);
                    pReadWritePoint->position = transform.position;

                    //pReadWritePoint->deltaRotationY = pReadWritePoint->deltaRotation = quaternion.identity;
                    pReadWritePoint->deltaPosition = float3.zero;

                }
            }
        }
        [BurstCompile]
        public struct InitiralizePoint2 : IJobParallelForTransform //OYM:再更新普通的点,避免出错
        {
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            public PointRead* pReadPoints;
            [NativeDisableUnsafePtrRestriction]
            public PointReadWrite* pReadWritePoints;

            public void Execute(int index, TransformAccess transform)
            {
                var pReadWritePoint = pReadWritePoints + index;
                var pReadPoint = pReadPoints + index;

                if (pReadPoint->parentIndex != -1)
                {
                    var pFixReadWritePoint = pReadWritePoints + (pReadPoint->fixedIndex);
                    var pFixReadPoint = pReadPoints + (pReadPoint->fixedIndex);
                    transform.localRotation = pReadPoint->initialLocalRotation;

                    pReadWritePoint->position = pFixReadWritePoint->position + math.mul(pFixReadWritePoint->rotationTemp, pReadPoint->initialPosition);

                    transform.position = pReadWritePoint->position;
                    pReadWritePoint->deltaPosition = float3.zero;

                }
            }
        }

        /// <summary>
        /// Collider计算AABB
        /// </summary>
        [BurstCompile]
        public struct ColliderGetAABB : IJobParallelFor
        //OYM：获取collider的deltaPostion
        {
            [NativeDisableUnsafePtrRestriction]
            public ColliderRead* pReadColliders;
            [ReadOnly]
            public float oneDivideIteration;
            [ReadOnly]
            public float globalScale;
            public void Execute(int index)
            {

                ColliderRead* pReadCollider = pReadColliders + index;
                float colliderScale = math.cmax(pReadCollider->scale);

                MinMaxAABB AABB, temp1, temp2;
                switch (pReadCollider->colliderType)
                {
                    case ColliderType.Sphere://OYM:包含上一帧的位置与这一帧的位置的球体的AABB

                        AABB = new MinMaxAABB(pReadCollider->fromPosition, pReadCollider->toPosition);
                        AABB.Expand(pReadCollider->radius * colliderScale);
                        break;
                    case ColliderType.Capsule://OYM:包含上一帧的位置与这一帧的位置的胶囊体的AABB
                        //OYM:这儿有点难,需要先判断两个AABB,然后形成一个更大的
                        temp1 = new MinMaxAABB(pReadCollider->fromPosition, pReadCollider->fromPosition + pReadCollider->fromDirection * pReadCollider->height * colliderScale); //OYM:起点形成的AABB
                        temp2 = new MinMaxAABB(pReadCollider->toPosition, pReadCollider->toPosition + pReadCollider->toDirection * pReadCollider->height * colliderScale); //OYM:终点形成的AABB
                        AABB = new MinMaxAABB(temp1, temp2);
                        AABB.Expand(pReadCollider->radius * colliderScale);
                        break;
                    case ColliderType.OBB://OYM:还好它有内置的旋转函数,否则不太好写

                        temp1 = MinMaxAABB.CreateFromCenterAndHalfExtents(pReadCollider->fromPosition, pReadCollider->boxSize * colliderScale); //OYM:创建一个与OBB大小一致的AABB
                        temp1 = MinMaxAABB.Rotate(pReadCollider->fromRotation, temp1);//OYM:进行旋转
                        temp2 = MinMaxAABB.CreateFromCenterAndHalfExtents(pReadCollider->toPosition, pReadCollider->boxSize * colliderScale); //OYM:创建一个与OBB大小一致的AABB
                        temp2 = MinMaxAABB.Rotate(pReadCollider->toRotation, temp2);//OYM:旋转它到OBB的位置,得到一个更大的AABB
                        AABB = new MinMaxAABB(temp1, temp2);//OYM:扩大包围盒
                        break;
                    default:
                        AABB = MinMaxAABB.identity;
                        break;
                }
                pReadCollider->AABB = AABB;
            }
        }


        /// <summary>
        /// 获取点的位置,同时处理速度上的一些调整
        /// </summary>
        [BurstCompile]
        public struct PointGetTransform : IJobParallelForTransform
        {
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            public PointRead* pReadPoints;
            [NativeDisableUnsafePtrRestriction]
            public PointReadWrite* pReadWritePoints;
            [ReadOnly]
            public float oneDivideIteration;
            public void Execute(int index, TransformAccess transform)
            {
                PointRead* pReadPoint = pReadPoints + index;
                PointReadWrite* pReadWritePoint = pReadWritePoints + index;
                quaternion transformRotation = transform.rotation;
                float3 transformPosition = transform.position;
                quaternion localRotation = transform.localRotation;
                //OYM：做笔记 unity当中 child.rotation =parent.rotation*child.localrotation;
                //OYM:假设子节点的localrotation不变，只有父节点的rotation变了，那么当前子节点的rotation应该是
                //OYM:Rotation*inverse（localRotation）*initialLocalRotation
                quaternion rotationTemp = math.mul(math.mul(transformRotation, math.inverse(localRotation)), pReadPoint->initialLocalRotation);
                if (pReadPoint->parentIndex == -1)//OYM：fixedpoint
                {
                    /*                   pReadWritePoint->deltaPosition = oneDivideIteration * (transformPosition - pReadWritePoint->position);
                                        pReadWritePoint->deltaRotation = math.nlerp(quaternion.identity, math.mul(rotationTemp, math.inverse(pReadWritePoint->rotationTemp)), oneDivideIteration);*/
                    pReadWritePoint->position += pReadWritePoint->deltaPosition;
                   pReadWritePoint->deltaPosition =  transformPosition - pReadWritePoint->position;

                    pReadWritePoint->deltaRotation = pReadWritePoint->rotationTemp;
                    pReadWritePoint->rotationTemp = rotationTemp;

                    for (int i = pReadPoint->childFirstIndex; i < pReadPoint->childLastIndex; i++)
                    {
                        PointReadWrite* pChildWritePoint = pReadWritePoints + i;
                        PointRead* pChildRead = pReadPoints + i;
                        pChildWritePoint->rotationTemp = rotationTemp; //OYM:子节点用到的还原旋转应该为这个值
                    }
                }
                else
                {
                    pReadPoint->massPerIteration = math.exp(math.log(0.8f + pReadPoint->mass) * oneDivideIteration);
                    //pReadWritePoint->deltaPosition *= (0.8f + pReadPoint->mass);

                    for (int i = pReadPoint->childFirstIndex; i < pReadPoint->childLastIndex; i++)
                    {
                        PointReadWrite* pChildWritePoint = pReadWritePoints + i;
                        PointRead* pChildRead = pReadPoints + i;
                        pChildWritePoint->rotationTemp = rotationTemp; //OYM:子节点用到的还原旋转应该为这个值
                    }
                    pReadWritePoint->physicProcess = 0;
                }
            }
        }

        [BurstCompile]
        public struct PointUpdate : IJobParallelFor
        {
            const float gravityLimit = 1f;
            /// <summary>
            /// 所有点位置的指针
            /// </summary>
            [NativeDisableUnsafePtrRestriction]
            internal PointReadWrite* pReadWritePoints;
            /// <summary>
            /// 所有点的指针
            /// </summary>
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            internal PointRead* pReadPoints;
            /// <summary>
            /// 所有碰撞体的指针
            /// </summary>);
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            public ColliderRead* pReadColliders;
            /// <summary>
            /// 碰撞体数量
            /// </summary>
            [ReadOnly]
            public int colliderCount;
            /// <summary>
            /// 风力
            /// </summary>
            [ReadOnly]
            internal float3 addForcePower;
            /// <summary>
            /// 大小
            /// </summary>
            [ReadOnly]
            internal float globalScale;
            /// <summary>
            /// 1/迭代次数,为什么不用迭代次数,因为除法比乘法慢
            /// </summary>
            [ReadOnly]
            internal float oneDivideIteration;
            [ReadOnly]
            internal float deltaTime;
            [ReadOnly]
            internal bool isCollision;
            [ReadOnly]
            internal bool isOptimize;

            public void Execute(int index)
            {
                PointRead* pReadPoint = pReadPoints + index;
                PointReadWrite* pReadWritePoint = pReadWritePoints + index;
                if (pReadPoint->fixedIndex != index)
                {
                    float process = pReadWritePoint->physicProcess;
                    EvaluatePosition(index, pReadPoint, pReadWritePoint);
                    if (isCollision)
                    {
                        for (int i = 0; i < colliderCount; ++i)
                        {
                            ColliderRead* pReadCollider = pReadColliders + i;

                            if (pReadCollider->isOpen && (pReadPoint->colliderChoice & pReadCollider->colliderChoice) != 0)
                            {

                                float pointRadius = pReadPoint->radius * globalScale;
                                bool isColliderInsideMode = (pReadCollider->collideFunc == CollideFunc.InsideLimit || pReadCollider->collideFunc == CollideFunc.InsideNoLimit); //OYM:用于判断是否需要翻转AABB结果
                                                                                                                                                                                //OYM:判断AABB
                                MinMaxAABB AABB = pReadCollider->AABB;
                                AABB.Expand(pointRadius);
                                if (!AABB.Contains(pReadWritePoint->position) ^ isColliderInsideMode)
                                {//OYM:这里有点难以理解，大概的思路是说，如果要求在外/在内的时候判断AABB发现必然在内/在外的情况下，结束判断
                                 //OYM:但是这里其实有个bug，如果你想要将粒子包含在碰撞体内，而粒子却恰好在AABB外，就会出现不判断的情况。
                                 //OYM:如果你发现这种情况一直存在，可以尝试将AABB扩大一倍。
                                    continue;
                                }

                                ColliderCheck(pReadPoint, pReadWritePoint, pReadCollider, pointRadius, isColliderInsideMode);
                            }
                        }
                    }
                    pReadWritePoint->physicProcess += oneDivideIteration;
                }
                /*  
                else
                {
                  //OYM：计算渐进的fixed点坐标
                    pReadWritePoint->position += pReadWritePoint->deltaPosition;
                    //pReadWritePoint->rotationY = math.mul(pReadWritePoint->deltaRotationY, pReadWritePoint->rotationY);
                    pReadWritePoint->rotationTemp = math.mul(pReadWritePoint->deltaRotation, pReadWritePoint->rotationTemp);
            }
                */
            }
            #region BaseForce
            private void EvaluatePosition(int index, PointRead* pReadPoint, PointReadWrite* pReadWritePoint)
            {
                //OYM：如果你想要添加什么奇怪的力的话,可以在这底下添加
                float3 position = pReadWritePoint->position;
                float3 deltaPosition = pReadWritePoint->deltaPosition;

                deltaPosition = deltaPosition * pReadPoint->massPerIteration;
                //OYM：获取固定点的信息

                if (pReadPoint->distanceCompensation != 0 || pReadPoint->moveByFixedPoint != 0)
                {
                    UpdateFixedPointChain(pReadPoint, pReadWritePoint, ref position, ref deltaPosition);//OYM:更新来自fixed节点的力
                }

                if (math.any(pReadPoint->gravity))
                {
                    UpdateGravity(pReadPoint, pReadWritePoint, ref deltaPosition);//OYM:更新重力
                }

                if (pReadPoint->freezeScale != 0)
                {
                    UpdateFreeze(pReadPoint, pReadWritePoint, ref position, ref deltaPosition);//OYM:更新复位力
                }

                if (math.any(addForcePower))
                {
                    UpdateExternalForce(pReadPoint, pReadWritePoint, ref deltaPosition); //OYM:更新额外的力
                }


                if (isOptimize)
                {
                    OptimeizeForce(pReadPoint, pReadWritePoint, ref position, ref deltaPosition); //OYM:一些实验性的优化,或许有用?
                }

                if (pReadPoint->rigidScale != 0)
                {
                    UpdateRigid(pReadPoint, pReadWritePoint, ref position, ref deltaPosition);
                }
                pReadWritePoint->deltaPosition = deltaPosition; //OYM:  赋值
                //OYM:以下部分会对deltaPosition更改,但不会影响其存储的值


                pReadWritePoint->position = position + oneDivideIteration * deltaPosition;//OYM：这里我想了很久,应该是这样,如果是迭代n次的话,那么deltaposition将会被加上n次,正规应该是只加一次
            }
            void UpdateRigid(PointRead* pReadPoint, PointReadWrite* pReadWritePoint, ref float3 position, ref float3 deltaPosition)//OYM:刚性，先放这里，修完碰撞体再来整理
            {
                PointReadWrite* pParentPointReadWrite = (pReadWritePoints + pReadPoint->parentIndex);
                PointRead* pParerntPointRead = (pReadPoints + pReadPoint->parentIndex);

                float3 parentPosition;
                quaternion parentRotation;

                if (pParerntPointRead->parentIndex == -1)
                {
                    parentPosition = pParentPointReadWrite->position + pParentPointReadWrite->deltaPosition * pReadWritePoint->physicProcess;
                    parentRotation = math.slerp(pParentPointReadWrite->deltaRotation, pParentPointReadWrite->rotationTemp, pReadWritePoint->physicProcess);
                }
                else
                {
                    parentPosition = pParentPointReadWrite->position;
                    parentRotation = pParentPointReadWrite->rotationTemp;
                }
                float3 tagetPosition = parentPosition + math.mul(parentRotation, pReadPoint->initialLocalPosition) * globalScale;
                float3 positionTemp = math.lerp(position, tagetPosition, pReadPoint->rigidScale * math.clamp(oneDivideIteration, 0, 0.5f));//OYM:这个值超过0.5之后会在单次迭代内出现奇怪的问题
                float3 force = positionTemp - position;
                position += force;

                //OYM:这里的想法是,在原有的速度上增加到目标速度就可以了,直接加速度的话会出现一些奇怪问题
                float persentage = math.dot(deltaPosition, force) / (math.lengthsq(force) + 1e-6f);
                persentage = 1 - math.clamp(persentage, 0, 1);
                //deltaPosition += force * persentage*deltaTime;
            }
            void UpdateFixedPointChain(PointRead* pReadPoint, PointReadWrite* pReadWritePoint, ref float3 position, ref float3 deltaPosition)
            {
                PointReadWrite* pFixedPointReadWrite = (pReadWritePoints + pReadPoint->fixedIndex);
                PointRead* pFixedPointRead = (pReadPoints + pReadPoint->fixedIndex);
                float3 fixedPointdeltaPosition = pFixedPointReadWrite->deltaPosition;
                position += pFixedPointReadWrite->deltaPosition * pReadPoint->distanceCompensation * oneDivideIteration;//OYM:计算速度补偿
                //OYM：计算以fixed位移进行为参考进行速度补偿
                deltaPosition -= pFixedPointReadWrite->deltaPosition * pReadPoint->moveByFixedPoint * 0.2f*oneDivideIteration;//OYM：测试了一下,0.2是个恰到好处的值,不会显得太大也不会太小
            }
            void UpdateGravity(PointRead* pReadPoint, PointReadWrite* pReadWritePoint, ref float3 deltaPosition)
            {
                float oldGravityForce = math.dot(deltaPosition, pReadPoint->gravity) / math.lengthsq(pReadPoint->gravity);
                if (oldGravityForce < gravityLimit)
                {
                    float3 gravity = pReadPoint->gravity * (deltaTime * deltaTime) * globalScale;//OYM：重力

                    deltaPosition += gravity * oneDivideIteration;
                }
                //OYM：获取归位的向量

            }
            void UpdateFreeze(PointRead* pReadPoint, PointReadWrite* pReadWritePoint, ref float3 position, ref float3 deltaPosition)
            {
                PointReadWrite* pFixedPointReadWrite = (pReadWritePoints + pReadPoint->fixedIndex);
                PointRead* pFixedPointRead = (pReadPoints + pReadPoint->fixedIndex);

                float3 fixedPointPosition = pFixedPointReadWrite->position + pFixedPointReadWrite->deltaPosition * pReadWritePoint->physicProcess; 
                float3 direction = position - fixedPointPosition;

                quaternion fixedPointRotation = math.slerp(pFixedPointReadWrite->rotationTemp, pFixedPointReadWrite->deltaRotation, pReadWritePoint->physicProcess);
                float3 originDirection = math.mul(fixedPointRotation, pReadPoint->initialPosition) * globalScale;

                float3 freezeForce = originDirection - direction;//OYM:因为direction+freezeForce=originDirection，所以freezeforce是这样算的
                freezeForce = math.clamp(freezeForce, -pReadPoint->freezeLimit, pReadPoint->freezeLimit);
                freezeForce = oneDivideIteration * deltaTime * pReadPoint->freezeScale * freezeForce;
                deltaPosition += freezeForce;
            }
            void UpdateExternalForce(PointRead* pReadPoint, PointReadWrite* pReadWritePoint, ref float3 deltaPosition)
            {
                float3 addForce = oneDivideIteration * addForcePower * pReadPoint->addForceScale / pReadPoint->weight;
                deltaPosition += addForce;
            }
            void OptimeizeForce(PointRead* pReadPoint, PointReadWrite* pReadWritePoint, ref float3 position, ref float3 deltaPosition)
            {
                float persentage;
                PointReadWrite* pFixedPointReadWrite = (pReadWritePoints + pReadPoint->fixedIndex);

                //OYM:限制速度
                persentage = math.sqrt(math.lengthsq(deltaPosition) / (math.lengthsq(pReadPoint->initialLocalPosition) * 0.2f + 1e-6f));//OYM:避免除以0
                if (persentage > 1)
                {
                    deltaPosition /= persentage;
                }

                //OYM：限制长度
                float3 direction = pReadWritePoint->position - pFixedPointReadWrite->position;
                persentage = math.sqrt(math.lengthsq(direction) / (math.lengthsq(pReadPoint->initialPosition) + 1e-6f));//OYM:计算速度与杆件长度的比值
                persentage = math.clamp(persentage - math.clamp(persentage, 0, 1f), 0, 1);//OYM:计算比值超出0-1的部分,限制到0-1内
                if (persentage != 0)
                {
                    float3 force = persentage * direction * oneDivideIteration;
                    deltaPosition -= force;
                }
            }
            float3 GetRotateForce(float3 force, float3 direction)//OYM：返回一个不存在的力,使得其向受力方向卷曲,在一些动漫里面会经常出现这种曲线的头发
            {
                var result = math.mul(quaternion.Euler(Mathf.Rad2Deg * force.z, 0, Mathf.Rad2Deg * force.x), direction) - direction;
                return new float3(result.x, 0, result.z);
            }
            #endregion

            #region Collision
            private void ColliderCheck(PointRead* pReadPoint, PointReadWrite* pReadWritePoint, ColliderRead* pReadCollider, float pointRadius, bool isColliderInsideMode)
            {

                float3 colliderscale3 = pReadCollider->scale;
                float colliderscale = math.cmax(colliderscale3);

                float3 pushout;
                float radiusSum;
                float process = pReadWritePoint->physicProcess;
                float3 colliderPosition = math.lerp(pReadCollider->fromPosition, pReadCollider->toPosition, process);



                //OYM:AABB判断在内
                switch (pReadCollider->colliderType)
                {
                    case ColliderType.Sphere: //OYM:球体
                        radiusSum = pReadCollider->radius * colliderscale + pointRadius;
                        pushout = pReadWritePoint->position - colliderPosition;
                        ClacPowerWhenCollision(pushout, radiusSum, pReadPoint, pReadWritePoint, pReadCollider->collideFunc);

                        break;

                    case ColliderType.Capsule: //OYM:胶囊体

                        float3 colliderDirection = math.lerp(pReadCollider->fromDirection, pReadCollider->toDirection, process);//OYM:渐进朝向
                        radiusSum = pointRadius + pReadCollider->radius * math.max(colliderscale3.x, colliderscale3.z);//OYM:选择collidersize3的x与collidersize3.z中最大的那个
                        pushout = pReadWritePoint->position - ConstrainToSegment(pReadWritePoint->position, colliderPosition, colliderDirection * pReadCollider->height * colliderscale3.y);
                        ClacPowerWhenCollision(pushout, radiusSum, pReadPoint, pReadWritePoint, pReadCollider->collideFunc);

                        break;
                    case ColliderType.OBB: //OYM:OBB

                        quaternion colliderRotation = math.nlerp(pReadCollider->fromRotation, pReadCollider->toRotation, process); //OYM:渐进旋转
                        var localPosition = math.mul(math.inverse(colliderRotation), (pReadWritePoint->position - colliderPosition)); //OYM:获取localPosition
                        MinMaxAABB localOBB = MinMaxAABB.CreateFromCenterAndHalfExtents(0, colliderscale3 * pReadCollider->boxSize + pointRadius);

                        if (localOBB.Contains(localPosition) ^ isColliderInsideMode)
                        {
                            if (isColliderInsideMode)
                            {
                                pushout = math.clamp(localPosition, localOBB.Min, localOBB.Max) - localPosition;
                            }
                            else
                            {
                                float3 toMax = localOBB.Max - localPosition;
                                float3 toMin = localOBB.Min - localPosition;
                                float3 min3 = new float3
                                (
                                math.abs(toMax.x) < math.abs(toMin.x) ? toMax.x : toMin.x,
                                math.abs(toMax.y) < math.abs(toMin.y) ? toMax.y : toMin.y,
                                math.abs(toMax.z) < math.abs(toMin.z) ? toMax.z : toMin.z
                                );
                                float3 min3Abs = math.abs(min3);
                                if (min3Abs.x <= min3Abs.y && min3Abs.x <= min3Abs.z)
                                {
                                    pushout = new float3(min3.x, 0, 0);
                                }
                                else if (min3Abs.y <= min3Abs.x && min3Abs.y <= min3Abs.z)
                                {
                                    pushout = new float3(0, min3.y, 0);
                                }
                                else
                                {
                                    pushout = new float3(0, 0, min3.z);
                                }
                            }

                            pushout = math.mul(colliderRotation, pushout);

                            DistributionPower(pushout, pReadPoint, pReadWritePoint, pReadCollider->collideFunc);

                        }
                        break;
                    default:
                        return;
                }
            }
            void ClacPowerWhenCollision(float3 pushout, float radius, PointRead* pReadPoint, PointReadWrite* pReadWritePoint, CollideFunc collideFunc)
            {
                float sqrPushout = math.lengthsq(pushout);
                switch (collideFunc)
                {
                    //OYM：整片代码里面最有趣的一块
                    //OYM：反正我现在不想回忆当时怎么想的了XD
                    case CollideFunc.OutsideLimit:
                        if ((sqrPushout > radius * radius) && sqrPushout != 0) //OYM:向外排斥:不允许radius小于Pushout
                        { return; }
                        break;
                    case CollideFunc.InsideLimit:
                        if (sqrPushout < radius * radius && sqrPushout != 0)//OYM:向内排斥:不允许radius大于Pushout
                        { return; }
                        break;
                    case CollideFunc.OutsideNoLimit://OYM:同向外排斥
                        if ((sqrPushout > radius * radius) && sqrPushout != 0)
                        { return; }
                        break;
                    case CollideFunc.InsideNoLimit://OYM:同向内排斥
                        if (sqrPushout < radius * radius && sqrPushout != 0)
                        { return; }
                        break;
                    default: { return; }

                }

                pushout = pushout * (radius / math.sqrt(sqrPushout) - 1);//OYM：这里简单解释一下,首先我要计算的是推出的距离,及半径长度减去原始的pushout度之后剩下的值,即pushout/pushout.magnitude*radius-pushout.即pushout*((radius/magnitude -1));
                DistributionPower(pushout, pReadPoint, pReadWritePoint, collideFunc);

            }
            void DistributionPower(float3 pushout, PointRead* pReadPoint, PointReadWrite* pReadWritePoint, CollideFunc collideFunc)
            {
                float sqrPushout = math.lengthsq(pushout);
                if (collideFunc == CollideFunc.InsideNoLimit || collideFunc == CollideFunc.OutsideNoLimit)
                {
                    pReadWritePoint->deltaPosition += 0.005f * oneDivideIteration * pReadPoint->addForceScale * pushout;
                }
                else
                {
                    pReadWritePoint->deltaPosition *= (1 - pReadPoint->friction * sqrPushout);
                    pReadWritePoint->position += pushout;
                    pReadWritePoint->deltaPosition += pushout;
                }


            }
            float3 ConstrainToSegment(float3 tag, float3 pos, float3 dir)
            {
                float t = math.dot(tag - pos, dir) / math.lengthsq(dir);
                return pos + dir * math.clamp(t, 0, 1);
            }
            #endregion
        }
        [BurstCompile]
        public struct ConstraintUpdate : IJobParallelFor
        {
            /// <summary>
            /// 指向所有可读的点
            /// </summary>);
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            public PointRead* pReadPoints;
            /// <summary>
            /// 指向所有可读写的点
            /// </summary>);
            [NativeDisableUnsafePtrRestriction]
            public PointReadWrite* pReadWritePoints;
            /// <summary>
            /// 所有可读的碰撞体
            /// </summary>);
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            public ColliderRead* pReadColliders;
            /// <summary>
            /// 所有杆件
            /// 
            /// </summary>
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            public ConstraintRead* pConstraintsRead;
            [ReadOnly]
            /// <summary>
            /// 碰撞体序号
            /// </summary>);
            public int colliderCount;
            [ReadOnly]
            public float globalScale;
            [ReadOnly]
            public int globalColliderCount;
            [ReadOnly]
            public bool isCollision;
            [ReadOnly]
            internal float oneDivideIteration;
#if ADB_DEBUG
            public void TryExecute(int index, int temp, JobHandle job)
            {
                if (!job.IsCompleted)
                {
                    job.Complete();
                }
                for (int i = 0; i < index; i++)
                {
                    Execute(i);
                }
            }
#endif
            public void Execute(int index)
            {
                //OYM：获取约束
                ConstraintRead* constraint = pConstraintsRead + index;

                //OYM：获取约束的节点AB
                PointRead* pPointReadA = pReadPoints + constraint->indexA;
                PointRead* pPointReadB = pReadPoints + constraint->indexB;
                if (pPointReadA->parentIndex == -1 && pPointReadB->parentIndex == -1)//OYM:都为fixed节点则不参与运算
                { return; }
                    //OYM：任意一点都不能小于极小值
                    //OYM：if ((WeightA <= EPSILON) && (WeightB <= EPSILON))
                    //OYM：获取可读写的点A
                    PointReadWrite* pReadWritePointA = pReadWritePoints + constraint->indexA;

                //OYM：获取可读写的点B
                PointReadWrite* pReadWritePointB = pReadWritePoints + constraint->indexB;
                //OYM：获取约束的朝向
                float3 positionA;
                float3 positionB;
                
                if (pPointReadA->parentIndex == -1)
                {
                    positionA = pReadWritePointA->position + pReadWritePointA->deltaPosition * pReadWritePointB->physicProcess;
                }
                else
                {
                    positionA = pReadWritePointA->position;
                }

                if (pPointReadB->parentIndex == -1)
                {
                    positionB = pReadWritePointB->position + pReadWritePointB->deltaPosition * pReadWritePointA->physicProcess;
                }
                else
                {
                    positionB = pReadWritePointB->position;
                }

                var Direction = positionB - positionA;
                if (math.all(Direction == 0))//OYM:所有的值都为0
                {
                    return;
                }
                float Distance = math.length(Direction);

                //OYM：力度等于距离减去长度除以弹性，这个值可以不存在，可以大于1但是没有什么卵用
                float Force = Distance - constraint->length * globalScale;
                //OYM：是否收缩，意味着力大于0
                bool IsShrink = Force >= 0.0f;
                float ConstraintPower;//OYM：这个值等于
                switch (constraint->type)
                //OYM：这下面都是一个意思，就是确认约束受到的力，然后根据这个获取杆件约束的属性，计算 ConstraintPower
                //OYM：Shrink为杆件全局值，另外两个值为线性插值获取的值，同理Stretch，所以这里大概可以猜中只是一个简单的不大于1的值
                {
                    case ConstraintType.Structural_Vertical:
                        ConstraintPower = IsShrink
                            ? constraint->shrink * (pPointReadA->structuralShrinkVertical + pPointReadB->structuralShrinkVertical)
                            : constraint->stretch * (pPointReadA->structuralStretchVertical + pPointReadB->structuralStretchVertical);
                        break;
                    case ConstraintType.Structural_Horizontal:
                        ConstraintPower = IsShrink
                            ? constraint->shrink * (pPointReadA->structuralShrinkHorizontal + pPointReadB->structuralShrinkHorizontal)
                            : constraint->stretch * (pPointReadA->structuralStretchHorizontal + pPointReadB->structuralStretchHorizontal);
                        break;
                    case ConstraintType.Shear:
                        ConstraintPower = IsShrink
                            ? constraint->shrink * (pPointReadA->shearShrink + pPointReadB->shearShrink)
                            : constraint->stretch * (pPointReadA->shearStretch + pPointReadB->shearStretch);
                        break;
                    case ConstraintType.Bending_Vertical:
                        ConstraintPower = IsShrink
                            ? constraint->shrink * (pPointReadA->bendingShrinkVertical + pPointReadB->bendingShrinkVertical)
                            : constraint->stretch * (pPointReadA->bendingStretchVertical + pPointReadB->bendingStretchVertical);
                        break;
                    case ConstraintType.Bending_Horizontal:
                        ConstraintPower = IsShrink
                            ? constraint->shrink * (pPointReadA->bendingShrinkHorizontal + pPointReadB->bendingShrinkHorizontal)
                            : constraint->stretch * (pPointReadA->bendingStretchHorizontal + pPointReadB->bendingStretchHorizontal);
                        break;
                    case ConstraintType.Circumference:
                        ConstraintPower = IsShrink
                            ? constraint->shrink * (pPointReadA->circumferenceShrink + pPointReadB->circumferenceShrink)
                            : constraint->stretch * (pPointReadA->circumferenceStretch + pPointReadB->circumferenceStretch);
                        break;
                    default:
                        ConstraintPower = 0.0f;
                        break;
                }


                //OYM：获取AB点重量比值的比值,由于重量越大移动越慢,所以A的值实际上是B的重量的比

                float WeightProportion = pPointReadB->weight / (pPointReadA->weight + pPointReadB->weight);

                if (ConstraintPower > 0.0f)//OYM：这里不可能小于0吧（除非有人搞破坏）
                {
                    float3 Displacement = Direction / Distance * (Force * ConstraintPower);

                    pReadWritePointA->position += Displacement * WeightProportion;
                    pReadWritePointA->deltaPosition += Displacement * WeightProportion;
                    pReadWritePointB->position += -Displacement * (1 - WeightProportion);
                    pReadWritePointB->deltaPosition += -Displacement * (1 - WeightProportion);
                }

                if (isCollision && constraint->isCollider)
                {
                    for (int i = 0; i < colliderCount; ++i)
                    {
                        ColliderRead* pReadCollider = pReadColliders + i;//OYM：终于到碰撞这里了

                        if (!(pReadCollider->isOpen && (pPointReadA->colliderChoice & pReadCollider->colliderChoice) != 0))
                        { continue; }//OYM：collider是否打开,且pPointReadA->colliderChoice是否包含 pReadCollider->colliderChoice的位

                        MinMaxAABB constraintAABB = new MinMaxAABB(positionA,positionB);
                        MinMaxAABB colliderAABB = pReadCollider->AABB;
                        constraintAABB.Expand(constraint->radius);
                        bool isColliderInsideMode = (pReadCollider->collideFunc == CollideFunc.InsideLimit || pReadCollider->collideFunc == CollideFunc.InsideNoLimit); //OYM:用于判断是否需要翻转AABB结果,参考点碰撞部分  

                        if (!colliderAABB.Overlaps(constraintAABB) ^ isColliderInsideMode) //OYM:overlap为假且isColliderInsideMode为真或者overlap为真且isColliderInsideMode为假
                        { continue; }

                        ComputeCollider(
                            pReadCollider,
                            pPointReadA, pPointReadB,
                            pReadWritePointA, pReadWritePointB, positionA, positionB,
                            constraint, constraintAABB,
                            WeightProportion, isColliderInsideMode
                            );
                    }
                }
            }
            #region Collision
            private void ComputeCollider(ColliderRead* pReadCollider,
                PointRead* pReadPointA, PointRead* pReadPointB,
                PointReadWrite* pReadWritePointA, PointReadWrite* pReadWritePointB,
                float3 positionA, float3 positionB, 
                ConstraintRead* constraint, MinMaxAABB constraintAABB, 
                float WeightProportion, bool isColliderInsideMode)
            {
                float throwTemp;//OYM:丢掉的数据,因为net4.0以下不支持_，为了避免这种情况就写上了
                float t, radius;
                float3 colliderScale3 = pReadCollider->scale;
                float colliderScale = math.cmax(colliderScale3);

                float process = (pReadWritePointA->physicProcess + pReadWritePointB->physicProcess) *0.5f;
                if (pReadPointA->parentIndex == -1|| pReadPointB->parentIndex == -1)//OYM:规范化process，
                {
                    process *= 2;
                }


                float3 colliderPosition = math.lerp(pReadCollider->fromPosition, pReadCollider->toPosition, process);

                switch (pReadCollider->colliderType)
                {
                    case ColliderType.Sphere:
                        {
                            radius = colliderScale * pReadCollider->radius + globalScale * constraint->radius;

                            {
                                float3 pointOnLine = ConstrainToSegment(colliderPosition, positionA, positionB - positionA, out t);
                                ClacPowerWhenCollision(pointOnLine - colliderPosition, radius,
                                    pReadPointA, pReadPointB, pReadWritePointA, pReadWritePointB,
                                    WeightProportion, t,
                                    pReadCollider->collideFunc);
                            }
                        }

                        break;
                    case ColliderType.Capsule:
                        {
                            radius = math.max(colliderScale3.x, colliderScale3.z) * pReadCollider->radius + globalScale * constraint->radius;
                            float3 colliderDirection = math.lerp(pReadCollider->fromDirection, pReadCollider->toDirection, process);

                            {
                                float3 pointOnCollider, pointOnLine;
                                SqrComputeNearestPoints(colliderPosition, colliderDirection * pReadCollider->height * colliderScale3.y, positionA, positionB - positionA, out throwTemp, out t, out pointOnCollider, out pointOnLine);
                                ClacPowerWhenCollision(pointOnLine - pointOnCollider, radius,
                                    pReadPointA, pReadPointB, pReadWritePointA, pReadWritePointB,
                                    WeightProportion, t,
                                    pReadCollider->collideFunc);
                            }
                        }

                        break;
                    case ColliderType.OBB:
                        {
                            quaternion colliderRotation = math.nlerp(pReadCollider->fromRotation, pReadCollider->toRotation, process);
                            float3 boxSize = colliderScale3 * pReadCollider->boxSize + new float3(globalScale * constraint->radius);

                            float t1, t2;
                            //OYM：这个方法可以求出直线与obbbox的两个交点
                            SegmentToOBB(positionA, positionB, colliderPosition, boxSize, math.inverse(colliderRotation), out t1, out t2);

                            t1 = Clamp01(t1);
                            t2 = Clamp01(t2);
                            //OYM：如果存在,那么t2>t1,且至少有一个点不在边界上
                            bool bHit = t1 >= 0f && t2 > t1 && t2 <= 1.0f;

                            if (bHit && !isColliderInsideMode) //OYM:判断杆件是否在胶囊体外
                            {
                                float3 pushout;
                                //OYM：这里不是取最近的点,而是取中点,最近的点效果并不理想
                                t = (t1 + t2) * 0.5f;
                                float3 dir = positionB - positionA;
                                float3 nearestPoint = positionA + dir * t;
                                pushout = math.mul(math.inverse(colliderRotation), (nearestPoint - colliderPosition));
                                float pushoutX = pushout.x > 0 ? boxSize.x - pushout.x : -boxSize.x - pushout.x;
                                float pushoutY = pushout.y > 0 ? boxSize.y - pushout.y : -boxSize.y - pushout.y;
                                float pushoutZ = pushout.z > 0 ? boxSize.z - pushout.z : -boxSize.z - pushout.z;
                                //OYM：这里我自己都不太记得了 XD
                                //OYM：这里是选推出点离的最近的位置,然后推出
                                //OYM：Abas(pushoutZ) < Abs(pushoutY)是错的 ,可能会出现两者都为0的情况
                                if (Abs(pushoutZ) <= Abs(pushoutY) && Abs(pushoutZ) <= Abs(pushoutX))
                                {
                                    pushout = math.mul(colliderRotation, new float3(0, 0, pushoutZ));

                                }
                                else if (Abs(pushoutY) <= Abs(pushoutX) && Abs(pushoutY) <= Abs(pushoutZ))
                                {
                                    pushout = math.mul(colliderRotation, new float3(0, pushoutY, 0));
                                }
                                else
                                {
                                    pushout = math.mul(colliderRotation, new float3(pushoutX, 0, 0));
                                }
                                DistributionPower(pushout,
                                pReadPointA, pReadPointB, pReadWritePointA, pReadWritePointB,
                                WeightProportion, t,
                                pReadCollider->collideFunc);

                            }
                            bool bOutside = t1 <= 0f || t1 >= 1f || t2 <= 0 || t2 >= 1f;
                            if (bOutside && isColliderInsideMode) //OYM:判断杆件是否有一部分在OBB边上或者外面
                            {

                                float3 localPositionA = math.mul(math.inverse(colliderRotation), positionA - colliderPosition);
                                float3 localPositionB = math.mul(math.inverse(colliderRotation), positionB - colliderPosition);
                                float3 pushA = math.clamp(localPositionA, -boxSize, boxSize) - localPositionA;
                                float3 pushB = math.clamp(localPositionB, -boxSize, boxSize) - localPositionB;

                                pushA = math.mul(colliderRotation, pushA);
                                pushB = math.mul(colliderRotation, pushB);
                                bool isFixedA = WeightProportion < 1e-6f;
                                if (!isFixedA) //OYM:A点可能固定
                                {
                                    if (pReadCollider->collideFunc == CollideFunc.InsideNoLimit)
                                    {
                                        pReadWritePointA->deltaPosition += 0.005f * oneDivideIteration * pReadPointA->addForceScale * pushA;
                                    }
                                    else
                                    {
                                        pReadWritePointA->deltaPosition *= (1 - pReadPointA->friction * math.lengthsq(pushA));//OYM:增加摩擦力,同时避免摩擦力过大

                                        positionA += pushA;
                                        pReadWritePointA->deltaPosition += pushA;
                                    }
                                }

                                if (pReadCollider->collideFunc == CollideFunc.InsideNoLimit) //OYM:B点一般不固定
                                {
                                    pReadWritePointB->deltaPosition += 0.005f * oneDivideIteration * pReadPointB->addForceScale * pushB;
                                }
                                else
                                {
                                    pReadWritePointB->deltaPosition *= (1 - pReadPointB->friction * math.lengthsq(pushB));//OYM:增加摩擦力,同时避免摩擦力过大

                                    positionB += pushB;
                                    pReadWritePointB->deltaPosition += pushB;
                                }

                            }

                            break;
                        }
                    default:
                        return;

                }
            }
            void ClacPowerWhenCollision(float3 pushout, float radius,
                 PointRead* pReadPointA, PointRead* pReadPointB,
                PointReadWrite* pReadWritePointA, PointReadWrite* pReadWritePointB,
                float WeightProportion, float lengthPropotion,
                CollideFunc collideFunc)
            {
                float sqrPushout = math.lengthsq(pushout);
                switch (collideFunc)
                {
                    //OYM：整片代码里面最有趣的一块
                    //OYM：反正我现在不想回忆当时怎么想的了XD
                    case CollideFunc.Freeze:
                        break;//OYM：猜猜为啥这样写
                    case CollideFunc.OutsideLimit:
                        if (!(sqrPushout < radius * radius) && sqrPushout != 0)
                        { return; }
                        break;
                    case CollideFunc.InsideLimit:
                        if (sqrPushout < radius * radius && sqrPushout != 0)
                        { return; }
                        break;
                    case CollideFunc.OutsideNoLimit:
                        if (!(sqrPushout < radius * radius) && sqrPushout != 0)
                        { return; }
                        break;
                    case CollideFunc.InsideNoLimit:
                        if (sqrPushout < radius * radius && sqrPushout != 0)
                        { return; }
                        break;

                }
                pushout = pushout * (radius / Mathf.Sqrt(sqrPushout) - 1);
                DistributionPower(pushout,
                    pReadPointA, pReadPointB, pReadWritePointA, pReadWritePointB,
                    WeightProportion, lengthPropotion,
                    collideFunc);
            }

            void DistributionPower(float3 pushout,
                 PointRead* pReadPointA, PointRead* pReadPointB, PointReadWrite* pReadWritePointA, PointReadWrite* pReadWritePointB,
                float WeightProportion, float lengthPropotion,
                CollideFunc collideFunc)
            {
                float sqrPushout = math.lengthsq(pushout);
                if (WeightProportion > 1e-6f)
                {
                    if (collideFunc == CollideFunc.InsideNoLimit || collideFunc == CollideFunc.OutsideNoLimit)
                    {
                        pReadWritePointA->deltaPosition += 0.005f * oneDivideIteration * (1 - lengthPropotion) * pReadPointA->addForceScale * pushout;
                    }
                    else
                    {
                        pReadWritePointA->deltaPosition *= (1 - pReadPointA->friction * sqrPushout);//OYM:增加摩擦力,同时避免摩擦力过大

                        pReadWritePointA->position += (pushout * (1 - lengthPropotion));
                        pReadWritePointA->deltaPosition += (pushout * (1 - lengthPropotion));
                    }
                }
                else
                {
                    lengthPropotion = 1;
                }

                if (collideFunc == CollideFunc.InsideNoLimit || collideFunc == CollideFunc.OutsideNoLimit)
                {
                    pReadWritePointB->deltaPosition += 0.005f * oneDivideIteration * (lengthPropotion) * pReadPointB->addForceScale * pushout;
                }
                else
                {

                    pReadWritePointB->deltaPosition *= (1 - pReadPointB->friction * sqrPushout);//OYM:增加摩擦力,同时避免摩擦力过大

                    pReadWritePointB->position += (pushout * lengthPropotion);
                    pReadWritePointB->deltaPosition += (pushout * lengthPropotion);
                }

            }
            //OYM：https://zalo.github.io/blog/closest-point-between-segments/#line-segments
            //OYM：目前是我见过最快的方法
            float SqrComputeNearestPoints(
                float3 posP,//OYM：碰撞体的位置起点位置
                float3 dirP,//OYM：碰撞体的朝向
                float3 posQ,//OYM：约束的起点坐标
                float3 dirQ,//OYM：约束的起点朝向
out float tP, out float tQ, out float3 pointOnP, out float3 pointOnQ)
            {
                float lineDirSqrMag = math.lengthsq(dirQ);
                float3 inPlaneA = posP - ((math.dot(posP - posQ, dirQ) / lineDirSqrMag) * dirQ);
                float3 inPlaneB = posP + dirP - ((math.dot(posP + dirP - posQ, dirQ) / lineDirSqrMag) * dirQ);
                float3 inPlaneBA = inPlaneB - inPlaneA;

                float t1 = math.dot(posQ - inPlaneA, inPlaneBA) / math.lengthsq(inPlaneBA);
                t1 = math.all(inPlaneA != inPlaneB) ? t1 : 0f; // Zero's t if parallel
                float3 L1ToL2Line = posP + dirP * Clamp01(t1);

                pointOnQ = ConstrainToSegment(L1ToL2Line, posQ, dirQ, out tQ);
                pointOnP = ConstrainToSegment(pointOnQ, posP, dirP, out tP);
                return math.lengthsq(pointOnP - pointOnQ);
            }

            float3 ConstrainToSegment(float3 tag, float3 pos, float3 dir, out float t)
            {
                t = math.dot(tag - pos, dir) / math.lengthsq(dir);
                t = Clamp01(t);
                return pos + dir * t;
            }
            void SegmentToOBB(float3 start, float3 end, float3 center, float3 size, quaternion InverseNormal, out float t1, out float t2)
            {
                float3 startP = math.mul(InverseNormal, (center - start));
                float3 endP = math.mul(InverseNormal, (center - end));
                SegmentToAABB(startP, endP, center, -size, size, out t1, out t2);
            }

            void SegmentToAABB(float3 start, float3 end, float3 center, float3 min, float3 max, out float t1, out float t2)
            {
                float3 dir = end - start;
                t1 = Max(
                                Min(
                                    (min.x - start.x) / dir.x,
                                    (max.x - start.x) / dir.x),
                                Min(
                                    (min.y - start.y) / dir.y,
                                    (max.y - start.y) / dir.y),
                                Min(
                                    (min.z - start.z) / dir.z,
                                    (max.z - start.z) / dir.z));
                t2 = Min(
                                Max(
                                    (min.x - start.x) / dir.x,
                                    (max.x - start.x) / dir.x),
                                Max(
                                    (min.y - start.y) / dir.y,
                                    (max.y - start.y) / dir.y),
                                Max(
                                    (min.z - start.z) / dir.z,
                                    (max.z - start.z) / dir.z));
            }
            float Abs(float A)
            {
                return A > 0 ? A : -A;
            }
            float Clamp01(float A)
            {
                return A > 0 ? (A < 1 ? A : 1) : 0;
            }
            float Min(float A, float B, float C)
            {
                return A < B ? (A < C ? A : C) : (B < C ? B : C);
            }
            float Min(float A, float B)
            {
                return A > B ? B : A;
            }
            float Max(float A, float B, float C)
            {
                return A > B ? (A > C ? A : C) : (B > C ? B : C);
            }
            float Max(float A, float B)
            {
                return A > B ? A : B;
            }
            #endregion
        }

        [BurstCompile]
        public struct ConstraintForceUpdateByPoint : IJobParallelFor //OYM:一个能避免粒子过于颤抖的 ConstraintForceUpdate,但是移除了 Constraint碰撞计算
        {
            /// <summary>
            /// 指向所有可读的点
            /// </summary>);
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            public PointRead* pReadPoints;
            /// <summary>
            /// 指向所有可读写的点
            /// </summary>);
            [NativeDisableUnsafePtrRestriction]
            public PointReadWrite* pReadWritePoints;

            /// <summary>
            /// 所有杆件
            /// </summary>
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            public NativeMultiHashMap<int, ConstraintRead> constraintsRead;

            [ReadOnly]
            public float globalScale;
            [ReadOnly]
            internal float oneDivideIteration;
            public void Execute(int index)
            {
                PointRead* pPointReadA = pReadPoints + index;
                if (pPointReadA->parentIndex < 0)//OYM:fixed节点不考虑受力
                {
                    return;
                }

                NativeMultiHashMapIterator<int> iterator;
                ConstraintRead constraint;
                float3 move = float3.zero;

                if (!constraintsRead.TryGetFirstValue(index, out constraint, out iterator)) //OYM:  在这里获取约束与迭代器
                {
                    return;
                }
                PointReadWrite* pReadWritePointA = pReadWritePoints + index;
                float3 positionA = pReadWritePointA->position;
                float process = pReadWritePointA->physicProcess;

                int count = 0;
                do
                {
                    count++;
                    //OYM：获取约束的节点AB
                    PointRead* pPointReadB = pReadPoints + constraint.indexB;
                    //OYM：任意一点都不能小于极小值
                    //OYM：if ((WeightA <= EPSILON) && (WeightB <= EPSILON))
                    //OYM：获取可读写的点B
                    PointReadWrite* pReadWritePointB = pReadWritePoints + constraint.indexB;

                    float3 positionB;
                    if (pPointReadB->parentIndex == -1)
                    {
                        positionB = pReadWritePointB->position + pReadWritePointB->deltaPosition *process;
                    }
                    else
                    {
                        positionB = pReadWritePointB->position;
                    }
                    //OYM：获取约束的朝向
                    var Direction = positionB - positionA;
                    if (math.all(Direction == 0))//OYM:所有的值都为0
                    {
                        continue;
                    }

                    float Distance = math.length(Direction);

                    //OYM：力度等于距离减去长度除以弹性，这个值可以不存在，可以大于1但是没有什么卵用
                    float Force = Distance - constraint.length * globalScale;
                    //OYM：是否收缩，意味着力大于0
                    bool IsShrink = Force >= 0.0f;
                    float ConstraintPower;//OYM：这个值等于
                    switch (constraint.type)
                    //OYM：这下面都是一个意思，就是确认约束受到的力，然后根据这个获取杆件约束的属性，计算 ConstraintPower
                    //OYM：Shrink为杆件全局值，另外两个值为线性插值获取的值，同理Stretch，所以这里大概可以猜中只是一个简单的不大于1的值
                    {
                        case ConstraintType.Structural_Vertical:
                            ConstraintPower = IsShrink
                                ? constraint.shrink * (pPointReadA->structuralShrinkVertical + pPointReadB->structuralShrinkVertical)
                                : constraint.stretch * (pPointReadA->structuralStretchVertical + pPointReadB->structuralStretchVertical);
                            break;
                        case ConstraintType.Structural_Horizontal:
                            ConstraintPower = IsShrink
                                ? constraint.shrink * (pPointReadA->structuralShrinkHorizontal + pPointReadB->structuralShrinkHorizontal)
                                : constraint.stretch * (pPointReadA->structuralStretchHorizontal + pPointReadB->structuralStretchHorizontal);
                            break;
                        case ConstraintType.Shear:
                            ConstraintPower = IsShrink
                                ? constraint.shrink * (pPointReadA->shearShrink + pPointReadB->shearShrink)
                                : constraint.stretch * (pPointReadA->shearStretch + pPointReadB->shearStretch);
                            break;
                        case ConstraintType.Bending_Vertical:
                            ConstraintPower = IsShrink
                                ? constraint.shrink * (pPointReadA->bendingShrinkVertical + pPointReadB->bendingShrinkVertical)
                                : constraint.stretch * (pPointReadA->bendingStretchVertical + pPointReadB->bendingStretchVertical);
                            break;
                        case ConstraintType.Bending_Horizontal:
                            ConstraintPower = IsShrink
                                ? constraint.shrink * (pPointReadA->bendingShrinkHorizontal + pPointReadB->bendingShrinkHorizontal)
                                : constraint.stretch * (pPointReadA->bendingStretchHorizontal + pPointReadB->bendingStretchHorizontal);
                            break;
                        case ConstraintType.Circumference:
                            ConstraintPower = IsShrink
                                ? constraint.shrink * (pPointReadA->circumferenceShrink + pPointReadB->circumferenceShrink)
                                : constraint.stretch * (pPointReadA->circumferenceStretch + pPointReadB->circumferenceStretch);
                            break;
                        default:
                            ConstraintPower = 0.0f;
                            break;
                    }

                    //OYM：获取AB点重量比值的比值,由于重量越大移动越慢,所以A的值实际上是B的重量的比

                    float WeightProportion = pPointReadB->weight / (pPointReadA->weight + pPointReadB->weight);

                    float3 Displacement = Direction / Distance * (Force * ConstraintPower);
                    move += Displacement * WeightProportion;

                } while (constraintsRead.TryGetNextValue(out constraint, ref iterator));
                if (count != 0)
                {
                    pReadWritePointA->deltaPosition += move / count;
                    pReadWritePointA->position += move / count;
                }
            }
        }
        [BurstCompile]
        public struct JobPointToTransform : IJobParallelForTransform
        //OYM：把job的点转换成实际的点
        {
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            public PointRead* pReadPoints;
            [NativeDisableUnsafePtrRestriction]
            public PointReadWrite* pReadWritePoints;
            [ReadOnly]
            public float deltaTime;

            public void Execute(int index, TransformAccess transform)
            {
#if ADB_DEBUG
             }
             public void TryExecute(TransformAccessArray transforms, JobHandle job)
             {
                 if (!job.IsCompleted)
                 {
                     job.Complete();
                 }
                 for (int i = 0; i < transforms.length; i++)
                 {
                     Execute(i, transforms[i]);
                 }
             }
             public void Execute(int index, Transform transform)
             {
#endif
                PointReadWrite* pReadWritePoint = pReadWritePoints + index;//OYM：获取每个读写点
                PointRead* pReadPoint = pReadPoints + index;//OYM：获取每个只读点

                if (pReadPoint->parentIndex != -1)//OYM：不是fix点
                {
                    transform.position = pReadWritePoint->position;
                }

                //OYM:  旋转节点
                //OYM:  这里有个bug,当初考虑的时候是存在多个子节点的,但是实际上并没有

                if (pReadPoint->childFirstIndex > -1 &&
                    !(pReadPoint->isFixedPointFreezeRotation && pReadPoint->parentIndex == -1))
                {
                    transform.localRotation = pReadPoint->initialLocalRotation;
                    int childCount = pReadPoint->childLastIndex - pReadPoint->childFirstIndex;
                    if (childCount > 1) return;

                    float3 ToDirection = 0;
                    float3 FromDirection = 0;
                    for (int i = pReadPoint->childFirstIndex; i < pReadPoint->childLastIndex; i++)
                    {
                        var targetChild = pReadWritePoints + i;
                        var targetChildRead = pReadPoints + i;
                        FromDirection += math.normalize(math.mul((quaternion)transform.rotation, targetChildRead->initialLocalPosition));//OYM：将BoneAxis按照transform.rotation进行旋转
                        ToDirection += math.normalize(targetChild->position - pReadWritePoint->position);//OYM：朝向等于面向子节点的方向

                    }

                    Quaternion AimRotation = FromToRotation(FromDirection, ToDirection);
                    transform.rotation = AimRotation * transform.rotation;
                }

            }

            public static quaternion FromToRotation(float3 from, float3 to, float t = 1.0f)
            {
                from = math.normalize(from);
                to = math.normalize(to);

                float cos = math.dot(from, to);
                float angle = math.acos(cos);
                float3 axis = math.cross(from, to);

                if (math.abs(1.0f + cos) < 1e-06f)
                {
                    angle = (float)math.PI;

                    if (from.x > from.y && from.x > from.z)
                    {
                        axis = math.cross(from, new float3(0, 1, 0));
                    }
                    else
                    {
                        axis = math.cross(from, new float3(1, 0, 0));
                    }
                }
                else if (math.abs(1.0f - cos) < 1e-06f)
                {
                    //angle = 0.0f;
                    //axis = new float3(1, 0, 0);
                    return quaternion.identity;
                }
                return quaternion.AxisAngle(math.normalize(axis), angle * t);
            }
        }
        #endregion
    }
}



/*        [BurstCompile]
        public struct ColliderGetTransform : IJobParallelForTransform
        //OYM：获取collider的deltaPostion
        {
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            public ColliderRead* pReadColliders;
            [NativeDisableUnsafePtrRestriction]
            public ColliderReadWrite* pReadWriteColliders;
            [ReadOnly]
            public float oneDivideIteration;
            [ReadOnly]
            public float globalScale;
            public void Execute(int index, TransformAccess transform)
            {
                ColliderReadWrite* pReadWriteCollider = pReadWriteColliders + index;
                ColliderRead* pReadCollider = pReadColliders + index;
                float colliderScale = pReadCollider->isConnectWithBody ? globalScale : 1;

                MinMaxAABB AABB;
                float3 currentPosition = (float3)transform.position + math.mul((quaternion)transform.rotation, pReadCollider->positionOffset);
                switch (pReadCollider->colliderType)
                {
                    case ColliderType.Sphere://OYM:包含上一帧的位置与这一帧的位置的球体的AABB

                        pReadWriteCollider->deltaPosition = oneDivideIteration * (currentPosition - pReadWriteCollider->position);
                        AABB = new MinMaxAABB(currentPosition, pReadWriteCollider->position);
                        AABB.Expand(pReadCollider->radius * colliderScale);
                        break;
                    case ColliderType.Capsule://OYM:包含上一帧的位置与这一帧的位置的胶囊体的AABB
                        //OYM:这儿有点难,需要先判断两个AABB,然后形成一个更大的
                        float3 currentDirection = math.mul((quaternion)transform.rotation, pReadCollider->staticDirection);
                        pReadWriteCollider->deltaPosition = oneDivideIteration * (currentPosition - pReadWriteCollider->position);
                        pReadWriteCollider->deltaDirection = oneDivideIteration * (currentDirection - pReadWriteCollider->direction);

                        MinMaxAABB temp1 = new MinMaxAABB(currentPosition, pReadWriteCollider->position); //OYM:起点形成的AABB
                        MinMaxAABB temp2 = new MinMaxAABB(currentPosition + currentDirection * pReadCollider->length, pReadWriteCollider->position + pReadWriteCollider->direction * pReadCollider->length); //OYM:终点形成的AABB
                        AABB = new MinMaxAABB(temp1, temp2);
                        AABB.Expand(pReadCollider->radius * colliderScale);

                        break;
                    case ColliderType.OBB://OYM:还好它有内置的旋转函数,否则不太好写
                        quaternion currentRotation = (transform.rotation * pReadCollider->staticRotation);
                        pReadWriteCollider->deltaPosition = oneDivideIteration * (currentPosition - pReadWriteCollider->position);
                        pReadWriteCollider->deltaRotation = math.nlerp(quaternion.identity, math.mul(currentRotation, math.inverse(pReadWriteCollider->rotation)), oneDivideIteration);

                        MinMaxAABB temp3 = MinMaxAABB.CreateFromCenterAndHalfExtents(currentPosition, pReadCollider->boxSize * colliderScale); //OYM:创建一个与OBB大小一致的AABB
                        MinMaxAABB temp4 = MinMaxAABB.Rotate(currentRotation, temp3);//OYM:旋转它到OBB的位置,得到一个更大的AABB
                        temp3 = MinMaxAABB.Rotate(pReadWriteCollider->rotation, temp3); //OYM:重复利用一下temp3,得到上一次位置的AABB
                        AABB = new MinMaxAABB(temp3, temp4);//OYM:扩大包围盒

                        break;
                    default:
                        AABB = new MinMaxAABB();
                        break;
                }
                pReadWriteCollider->AABB = AABB;
            }
        }*/

/*        [BurstCompile]
        public struct ColliderUpdate : IJobParallelFor
        //OYM：把job的点转换成实际的点
        {
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            public ColliderRead* pReadColliders;
            [NativeDisableUnsafePtrRestriction]
            public ColliderReadWrite* pReadWriteColliders;
            public void TryExecute(int index, int _, JobHandle job)
            {
                if (!job.IsCompleted)
                {
                    job.Complete();
                }
                for (int i = 0; i < index; i++)
                {
                    Execute(i);
                }
            }
            public void Execute(int index)
            {
                ColliderReadWrite* pReadWriteCollider = pReadWriteColliders + index;
                ColliderRead* pReadCollider = pReadColliders + index;
                switch (pReadCollider->colliderType)
                {
                    case ColliderType.Sphere:
                        pReadWriteCollider->position += pReadWriteCollider->deltaPosition;
                        break;
                    case ColliderType.Capsule:
                        pReadWriteCollider->position += pReadWriteCollider->deltaPosition;
                        pReadWriteCollider->direction += pReadWriteCollider->deltaDirection;
                        break;
                    case ColliderType.OBB:
                        pReadWriteCollider->position += pReadWriteCollider->deltaPosition;
                        pReadWriteCollider->rotation = math.mul(pReadWriteCollider->deltaRotation, pReadWriteCollider->rotation);
                        break;
                    default:
                        break;

                }
            }
        }*/
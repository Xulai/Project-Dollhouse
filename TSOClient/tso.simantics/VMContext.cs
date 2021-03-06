﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using tso.world;
using TSO.Simantics.engine;
using TSO.Simantics.engine.primitives;
using TSO.Simantics.primitives;
using TSO.Content;
using TSO.Files.formats.iff;
using tso.world.model;
using tso.world.components;
using TSO.Files.formats.iff.chunks;
using Microsoft.Xna.Framework;
using TSO.Simantics.model;
using TSO.Simantics.entities;

namespace TSO.Simantics
{
    public class VMContext
    {
        public Blueprint Blueprint;
        public VMClock Clock { get; internal set; }
        public World World { get; internal set; }
        public Dictionary<ushort, VMPrimitiveRegistration> Primitives = new Dictionary<ushort, VMPrimitiveRegistration>();
        public VMAmbientSound Ambience;
        private ulong RandomSeed;

        public GameGlobal Globals;
        public VMRoomInfo[] RoomInfo;
        private Dictionary<VMTilePos, List<short>> ObjectsAt; //used heavily for routing
        
        public VM VM;

        public VMContext(World world){
            this.World = world;
            this.Clock = new VMClock();
            this.Ambience = new VMAmbientSound();

            ObjectsAt = new Dictionary<VMTilePos, List<short>>();

            RandomSeed = (ulong)((new Random()).NextDouble() * UInt64.MaxValue); //when resuming state, this should be set.
            Clock.TicksPerMinute = 30; //1 minute per irl second

            AddPrimitive(new VMPrimitiveRegistration(new VMGenericTSOCall())
            {
                Opcode = 1,
                Name = "generic_sims_online_call",
                OperandModel = typeof(VMGenericTSOCallOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMGotoRoutingSlot()) {
                Opcode = 45,
                Name = "goto_routing_slot",
                OperandModel = typeof(VMGotoRoutingSlotOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMAnimateSim()) {
                Opcode = 44,
                Name = "animate",
                OperandModel = typeof(VMAnimateSimOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMRelationship())
            {
                Opcode = 24,
                Name = "old_relationship",
                OperandModel = typeof(VMOldRelationshipOperand) //same primitive, different operand
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMRelationship())
            {
                Opcode = 26,
                Name = "relationship",
                OperandModel = typeof(VMRelationshipOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMFindLocationFor())
            {
                Opcode = 16,
                Name = "find_location_for",
                OperandModel = typeof(VMFindLocationForOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMTestSimInteractingWith()) {
                Opcode = 37,
                Name = "test_sim_interacting_with",
                OperandModel = typeof(VMTestSimInteractingWithOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMFindBestAction())
            {
                Opcode = 65,
                Name = "find_best_action",
                OperandModel = typeof(VMFindBestActionOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMGrab())
            {
                Opcode = 4,
                Name = "grab",
                OperandModel = typeof(VMGrabOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMDropOnto())
            {
                Opcode = 43,
                Name = "drop_onto",
                OperandModel = typeof(VMDropOntoOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMReach())
            {
                Opcode = 47,
                Name = "reach",
                OperandModel = typeof(VMReachOperand)
            });


            AddPrimitive(new VMPrimitiveRegistration(new VMLookTowards())
            {
                Opcode = 22,
                Name = "look_towards",
                OperandModel = typeof(VMLookTowardsOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMSysLog())
            {
                Opcode = 30,
                Name = "syslog",
                OperandModel = typeof(VMSysLogOperand)
            });


            AddPrimitive(new VMPrimitiveRegistration(new VMGetDistanceTo())
            {
                Opcode = 11,
                Name = "get_distance_to",
                OperandModel = typeof(VMGetDistanceToOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMGetDirectionTo())
            {
                Opcode = 12,
                Name = "get_direction_to",
                OperandModel = typeof(VMGetDirectionToOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMBreakPoint())
            {
                Opcode = 15,
                Name = "breakpoint",
                OperandModel = typeof(VMBreakPointOperand)
            });


            AddPrimitive(new VMPrimitiveRegistration(new VMChangeActionString())
            {
                Opcode = 50,
                Name = "change_action_string",
                OperandModel = typeof(VMChangeActionStringOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMSnap()) //not functional right now
            {
                Opcode = 46,
                Name = "snap",
                OperandModel = typeof(VMSnapOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMCreateObjectInstance())
            {
                Opcode = 42,
                Name = "create_object_instance",
                OperandModel = typeof(VMCreateObjectInstanceOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMRemoveObjectInstance())
            {
                Opcode = 18,
                Name = "remove_object_instance",
                OperandModel = typeof(VMRemoveObjectInstanceOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMIdleForInput())
            {
                Opcode = 17,
                Name = "idle_for_input",
                OperandModel = typeof(VMIdleForInputOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMRunTreeByName())
            {
                Opcode = 28,
                Name = "run_tree_by_name",
                OperandModel = typeof(VMRunTreeByNameOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMPushInteraction())
            {
                Opcode = 13,
                Name = "push_interaction",
                OperandModel = typeof(VMPushInteractionOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMFindBestObjectForFunction())
            {
                Opcode = 14,
                Name = "find_best_object_for_function",
                OperandModel = typeof(VMFindBestObjectForFunctionOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration(new VMRunFunctionalTree())
            {
                Opcode = 20,
                Name = "run_functional_tree",
                OperandModel = typeof(VMRunFunctionalTreeOperand)
            });

            AddPrimitive(new VMPrimitiveRegistration (new VMExpression()) {
                Opcode = 2,
                Name = "expression",
                OperandModel = typeof(VMExpressionOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMRandomNumber())
            {
                Opcode = 8,
                Name = "random_number",
                OperandModel = typeof(VMRandomNumberOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMPlaySound())
            {
                Opcode = 23,
                Name = "play_sound",
                OperandModel = typeof(VMPlaySoundOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMSleep())
            {
                Opcode = 0,
                Name = "sleep",
                OperandModel = typeof(VMSleepOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMRefresh())
            {
                Opcode = 7,
                Name = "refresh",
                OperandModel = typeof(VMRefreshOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMNotifyOutOfIdle())
            {
                Opcode = 49,
                Name = "stackobj_notify_out_of_idle",
                OperandModel = typeof(VMAnimateSimOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMSetMotiveChange())
            {
                Opcode = 29,
                Name = "set_motive_deltas",
                OperandModel = typeof(VMSetMotiveChangeOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMChangeSuitOrAccessory())
            {
                Opcode = 6,
                Name = "change_suit_or_accessory",
                OperandModel = typeof(VMChangeSuitOrAccessoryOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMTransferFunds())
            {
                Opcode = 25,
                Name = "transfer_funds",
                OperandModel = typeof(VMTransferFundsOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMSetToNext())
            {
                Opcode = 31,
                Name = "set_to_next",
                OperandModel = typeof(VMSetToNextOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMGotoRelativePosition())
            {
                Opcode = 27,
                Name = "goto_relative",
                OperandModel = typeof(VMGotoRelativePositionOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMTestObjectType())
            {
                Opcode = 32,
                Name = "test_object_type",
                OperandModel = typeof(VMTestObjectTypeOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMSpecialEffect())
            {
                Opcode = 35,
                Name = "special_effect",
                OperandModel = typeof(VMSpecialEffectOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMSetBalloonHeadline())
            {
                Opcode = 41,
                Name = "set_balloon_headline",
                OperandModel = typeof(VMSetBalloonHeadlineOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMDialogPrivateStrings())
            {
                Opcode = 36,
                Name = "dialog_private",
                OperandModel = typeof(VMDialogStringsOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMDialogSemiGlobalStrings())
            {
                Opcode = 39,
                Name = "dialog_semiglobal",
                OperandModel = typeof(VMDialogStringsOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMDialogGlobalStrings())
            {
                Opcode = 38,
                Name = "dialog_global",
                OperandModel = typeof(VMDialogStringsOperand)
            });
            AddPrimitive(new VMPrimitiveRegistration(new VMStopAllSounds())
            {
                Opcode = 48,
                Name = "stop_all_sounds",
                OperandModel = typeof(VMStopAllSoundsOperand)
            });
        }

        /// <summary>
        /// Returns a random number between 0 and less than the specified maximum.
        /// </summary>
        /// <param name="max">The upper bound of the random number.</param>
        /// <returns></returns>
        public ulong NextRandom(ulong max)
        {
            if (max == 0) return 0;
            RandomSeed = (RandomSeed * 274876858367) + 1046527;
            return RandomSeed % max;
        }

        public void RegeneratePortalInfo()
        {
            RoomInfo = new VMRoomInfo[Blueprint.RoomData.Count()];
            for (int i = 0; i < RoomInfo.Length; i++)
            {
                RoomInfo[i].Portals = new List<VMRoomPortal>();
            }

            foreach (var obj in VM.Entities)
            {
                if (obj.EntryPoints[15].ActionFunction != 0)
                { //portal object
                    AddRoomPortal(obj);
                }
            }
        }

        public void AddRoomPortal(VMEntity obj)
        {
            var room = GetObjectRoom(obj);

            //find other portal part, must be in other room to count...
            foreach (var obj2 in obj.MultitileGroup.Objects)
            {
                var room2 = GetObjectRoom(obj2);
                if (obj != obj2 && room2 != room && obj2.EntryPoints[15].ActionFunction != 0)
                {
                    RoomInfo[room].Portals.Add(new VMRoomPortal(obj.ObjectID, room2));
                    break;
                }
            }
        }

        public void RemoveRoomPortal(VMEntity obj)
        {
            var room = GetObjectRoom(obj);
            VMRoomPortal target = null;
            foreach (var port in RoomInfo[room].Portals)
            {
                if (port.ObjectID == obj.ObjectID)
                {
                    target = port;
                    break;
                }
            }
            if (target != null) RoomInfo[room].Portals.Remove(target);
        }

        public void RegisterObjectPos(VMEntity obj)
        {
            var pos = new VMTilePos(obj.WorldUI.TileX, obj.WorldUI.TileY, obj.WorldUI.Level);
            if (!ObjectsAt.ContainsKey(pos)) ObjectsAt[pos] = new List<short>();
            ObjectsAt[pos].Add(obj.ObjectID);
        }

        public void UnregisterObjectPos(VMEntity obj)
        {
            var pos = new VMTilePos(obj.WorldUI.TileX, obj.WorldUI.TileY, obj.WorldUI.Level);
            ObjectsAt[pos].Remove(obj.ObjectID);
        }

        public VMSolidResult SolidToAvatars(VMTilePos pos)
        {
            if (!ObjectsAt.ContainsKey(pos)) return new VMSolidResult();
            var objs = ObjectsAt[pos];
            foreach (var id in objs)
            {
                var obj = VM.GetObjectById(id);
                var flags = (VMEntityFlags)obj.GetValue(VMStackObjectVariable.Flags);
                if (((flags & VMEntityFlags.DisallowPersonIntersection) > 0) || (flags & (VMEntityFlags.AllowPersonIntersection | VMEntityFlags.HasZeroExtent)) == 0) 
                    return new VMSolidResult { 
                        Solid = true,
                        Chair = (obj.EntryPoints[26].ActionFunction != 0)?obj:null
                    }; //solid to people
            }
            return new VMSolidResult();;
        }

        public ushort GetObjectRoom(VMEntity obj)
        {
            return Blueprint.Rooms.Map[(int)(obj.Position.X+0.5) + (int)(obj.Position.Y+0.5)*Blueprint.Width];
        }

        public ushort GetRoomAt(Vector3 pos)
        {
            if (pos.X < 0 || pos.X >= Blueprint.Width) return 0;
            else if (pos.Y < 0 || pos.Y >= Blueprint.Height) return 0;
            else return Blueprint.Rooms.Map[(int)(pos.X) + (int)(pos.Y) * Blueprint.Width];
        }

        public VMEntity CreateObjectInstance(UInt32 GUID, short x, short y, sbyte level, Direction direction)
        {

            var objDefinition = TSO.Content.Content.Get().WorldObjects.Get(GUID);
            if (objDefinition == null)
            {
                return null;
            }

            var master = objDefinition.OBJ.MasterID;
            if (master != 0)
            {
                var objd = objDefinition.Resource.List<OBJD>();
                VMMultitileGroup group = new VMMultitileGroup();

                for (int i = 0; i < objd.Count; i++)
                {
                    if (objd[i].MasterID == master && objd[i].SubIndex != -1) //if sub-part of this object, make it!
                    {
                        var subObjDefinition = TSO.Content.Content.Get().WorldObjects.Get(objd[i].GUID);
                        if (subObjDefinition != null)
                        {
                            var worldObject = new ObjectComponent(subObjDefinition);
                            var vmObject = new VMGameObject(subObjDefinition, worldObject);
                            vmObject.MasterDefinition = objDefinition.OBJ;
                            vmObject.UseTreeTableOf(objDefinition);
                            group.Objects.Add(vmObject);

                            VM.AddEntity(vmObject);

                            vmObject.MultitileGroup = group;
                        }
                    }
                }

                group.ChangePosition(x, y, level, direction, this);
                return (VMGameObject)group.Objects[0];
            }
            else
            {
                if (objDefinition.OBJ.ObjectType == OBJDType.Person) //person
                {
                    var vmObject = new VMAvatar(objDefinition);
                    VM.AddEntity(vmObject);

                    //this.InitWorldComponent(vmObject.WorldUI);
                    Blueprint.AddAvatar((AvatarComponent)vmObject.WorldUI);

                    vmObject.SetPosition(x, y, level, direction, this);
                    return vmObject;
                }
                else
                {
                    var worldObject = new ObjectComponent(objDefinition);
                    var vmObject = new VMGameObject(objDefinition, worldObject);

                    VM.AddEntity(vmObject);

                    vmObject.SetPosition(x, y, level, direction, this);
                    return vmObject;
                }
            }
        }

        public void RemoveObjectInstance(VMEntity target)
        {
            VM.RemoveEntity(target);
            if (target is VMGameObject) Blueprint.RemoveObject((ObjectComponent)target.WorldUI);
            else Blueprint.RemoveAvatar((AvatarComponent)target.WorldUI);
        }

        public VMPrimitiveRegistration GetPrimitive(ushort opcode)
        {
            if (Primitives.ContainsKey(opcode)){
                return Primitives[opcode];
            }
            return null;
        }

        public void AddPrimitive(VMPrimitiveRegistration primitive){
            Primitives.Add(primitive.Opcode, primitive);
        }

        public void ThreadIdle(VMThread thread){
            /** Switch thread to idle **/
            VM.ThreadIdle(thread);
        }

        public void ThreadActive(VMThread thread){
            /** Switch thread to active **/
            VM.ThreadActive(thread);
        }

        public void ThreadRemove(VMThread thread)
        {
            /** Stop updating a thread **/
            VM.ThreadRemove(thread);
        }
    }

    public struct VMSolidResult
    {
        public bool Solid;
        public VMEntity Chair;
    }

    public struct VMTilePos
    {
        public short X;
        public short Y;
        public sbyte Level;

        public VMTilePos(short x, short y, sbyte level)
        {
            X = x; Y = y; Level = level;
        }
    }
}

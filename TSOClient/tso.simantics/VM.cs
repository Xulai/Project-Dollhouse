﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TSO.Files.formats.iff.chunks;
using TSO.Simantics.engine;
using Microsoft.Xna.Framework;
using TSO.Content;
using TSO.Vitaboy;

namespace TSO.Simantics
{
    /// <summary>
    /// Simantics Virtual Machine.
    /// </summary>
    public class VM
    {
        private const long TickInterval = 33 * TimeSpan.TicksPerMillisecond;

        public VMContext Context { get; internal set; }
        public List<VMEntity> Entities = new List<VMEntity>();
        public short[] GlobalState;

        private object ThreadLock;
        //This is a hash set to avoid duplicates which would cause objects to get multiple ticks per VM tick **/
        private HashSet<VMThread> ActiveThreads = new HashSet<VMThread>();
        private HashSet<VMThread> IdleThreads = new HashSet<VMThread>();
        private List<VMStateChangeEvent> ThreadEvents = new List<VMStateChangeEvent>();

        private Dictionary<short, VMEntity> ObjectsById = new Dictionary<short, VMEntity>();
        //This will need to be an int or long when a server is introduced **/
        //noooope object ids definitely need to be shorts. I don't ever see people having 65536 objects anyways.
        private short ObjectId = 1;

        /// <summary>
        /// Constructs a new Virtual Machine instance.
        /// </summary>
        /// <param name="context">The VMContext instance to use.</param>
        public VM(VMContext context)
        {
            context.VM = this;
            ThreadLock = this;
            this.Context = context;
        }

        /// <summary>
        /// Gets an entity from this VM.
        /// </summary>
        /// <param name="id">The entity's ID.</param>
        /// <returns>A VMEntity instance associated with the ID.</returns>
        public VMEntity GetObjectById(short id)
        {
            if (ObjectsById.ContainsKey(id))
            {
                return ObjectsById[id];
            }
            return null;
        }

        /// <summary>
        /// Initializes this Virtual Machine.
        /// </summary>
        public void Init()
        {
            Context.Globals = TSO.Content.Content.Get().WorldObjectGlobals.Get("global");
            GlobalState = new short[33];
            GlobalState[20] = 255; //Game Edition. Basically, what "expansion packs" are running. Let's just say all of them.
            GlobalState[25] = 4; //as seen in edith's simulator globals, this needs to be set for people to do their idle interactions.
            GlobalState[17] = 4; //Runtime Code Version, is this in EA-Land.
        }

        /// <summary>
        /// Idles a thread.
        /// </summary>
        /// <param name="thread">The thread to idle.</param>
        public void ThreadIdle(VMThread thread)
        {
            ThreadEvents.Add(new VMStateChangeEvent 
            {
                NewState = VMThreadState.Idle,
                Thread = thread
            });
        }

        /// <summary>
        /// Actives a thread.
        /// </summary>
        /// <param name="thread">The thread to active.</param>
        public void ThreadActive(VMThread thread)
        {
            ThreadEvents.Add(new VMStateChangeEvent
            {
                NewState = VMThreadState.Active,
                Thread = thread
            });
        }

        /// <summary>
        /// Removes a thread.
        /// </summary>
        /// <param name="thread">The thread to remove.</param>
        public void ThreadRemove(VMThread thread)
        {
            ThreadEvents.Add(new VMStateChangeEvent
            {
                NewState = VMThreadState.Removed,
                Thread = thread
            });
        }

        private long LastTick = 0;
        public void Update(GameTime time)
        {
            if (LastTick == 0 || (time.TotalGameTime.Ticks - LastTick) >= TickInterval)
            {
                Tick(time);
            }
            else
            {
                //fractional animation for avatars
                foreach (var obj in Entities)
                {
                    if (obj is VMAvatar) ((VMAvatar)obj).FractionalAnim(0.5f); 
                }
            }
        }
        private void Tick(GameTime time)
        {
            Context.Clock.Tick();
            //System.Diagnostics.Debug.WriteLine("VM Tick");

            lock (ThreadLock)
            {
                foreach (var evt in ThreadEvents)
                {
                    switch (evt.NewState){
                        case VMThreadState.Idle:
                            evt.Thread.State = VMThreadState.Idle;
                            IdleThreads.Add(evt.Thread);
                            ActiveThreads.Remove(evt.Thread);
                            break;
                        case VMThreadState.Active:
                            evt.Thread.State = VMThreadState.Active;
                            IdleThreads.Remove(evt.Thread);
                            ActiveThreads.Add(evt.Thread);
                            break;
                        case VMThreadState.Removed:
                            if (evt.Thread.State == VMThreadState.Active) ActiveThreads.Remove(evt.Thread);
                            else IdleThreads.Remove(evt.Thread);
                            evt.Thread.State = VMThreadState.Removed;
                            break;
                    }
                }

                ThreadEvents.Clear();

                LastTick = time.TotalGameTime.Ticks;
                foreach (var thread in ActiveThreads)
                {
                    thread.Tick();
                }

                foreach (var obj in Entities)
                {
                    obj.Tick(); //run object specific tick behaviors, like lockout count decrement
                }
            }
        }

        /// <summary>
        /// Adds an entity to this Virtual Machine.
        /// </summary>
        /// <param name="entity">The entity to add.</param>
        public void AddEntity(VMEntity entity)
        {
            this.Entities.Add(entity);
            entity.ObjectID = ObjectId++;
            ObjectsById.Add(entity.ObjectID, entity);
            entity.Init(Context);
        }

        /// <summary>
        /// Removes an entity from this Virtual Machine.
        /// </summary>
        /// <param name="entity">The entity to remove.</param>
        public void RemoveEntity(VMEntity entity)
        {
            if (Entities.Contains(entity))
            {
                this.Entities.Remove(entity);
                ObjectsById.Remove(entity.ObjectID);
            }
            entity.Dead = true;
        }

        /// <summary>
        /// Gets a global value set for this Virtual Machine.
        /// </summary>
        /// <param name="var">The index of the global value to get. WARNING: Throws exception if index is OOB.
        /// Must be in range of 0 - 31.</param>
        /// <returns>A global value if found.</returns>
        public short GetGlobalValue(ushort var)
        {
            if (var > 32) throw new Exception("Global Access out of bounds!");
            return GlobalState[var];
        }

        /// <summary>
        /// Sets a global value for this Virtual Machine.
        /// </summary>
        /// <param name="var">Index for value, must be in range 0 - 31.</param>
        /// <param name="value">Global value.</param>
        /// <returns>True if successful. WARNING: If index was OOB, exception is thrown.</returns>
        public bool SetGlobalValue(ushort var, short value)
        {
            if (var > 32) throw new Exception("Global Access out of bounds!");
            GlobalState[var] = value;
            return true;
        }

        private static Dictionary<BHAV, VMRoutine> _Assembled = new Dictionary<BHAV, VMRoutine>();

        /// <summary>
        /// Assembles a set of instructions.
        /// </summary>
        /// <param name="bhav">The instruction set to assemble.</param>
        /// <returns>A VMRoutine instance.</returns>
        public VMRoutine Assemble(BHAV bhav)
        {
            lock (_Assembled)
            {
                if (_Assembled.ContainsKey(bhav))
                {
                    return _Assembled[bhav];
                }
                var routine = VMTranslator.Assemble(this, bhav);
                _Assembled.Add(bhav, routine);
                return routine;
            }
        }
    }

    /// <summary>
    /// Event thrown on VM state change.
    /// </summary>
    public class VMStateChangeEvent
    {
        public VMThread Thread;
        public VMThreadState NewState;
    }
}

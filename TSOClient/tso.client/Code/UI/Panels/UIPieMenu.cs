﻿/*This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.

The Original Code is the TSOClient.

The Initial Developer of the Original Code is
RHY3756547. All Rights Reserved.

Contributor(s): ______________________________________.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TSOClient.Code.UI.Framework;
using TSOClient.Code.UI.Panels;
using TSOClient.Code.UI.Model;
using TSOClient.Code.UI.Controls;
using TSOClient.LUI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TSOClient.Code.Utils;
using TSO.Simantics;
using TSO.HIT;
using TSO.Vitaboy;
using TSO.Common.rendering.framework.camera;
using TSO.Common.rendering.framework;
using tso.common.utils;

namespace TSOClient.Code.UI.Panels
{
    public class UIPieMenu : UIContainer
    {
        public UIPieMenuItem m_PieTree;
        public List<UIButton> m_PieButtons;
        public UIPieMenuItem m_CurrentItem;
        public VMEntity m_Obj;
        public VMEntity m_Caller;
        public UILotControl m_Parent;
        public UIImage m_Bg;

        private _3DScene HeadScene;
        private BasicCamera HeadCamera;
        private double m_BgGrow;

        //This is a standard AdultVitaboyModel instance. Since nothing is needed but the head for pie menus,
        //the other parts of the body will be stripped from it (see constructor).
        private SimAvatar m_Head;

        private TextStyle ButtonStyle;

        public UIPieMenu(List<VMPieMenuInteraction> pie, VMEntity obj, VMEntity caller, UILotControl parent)
        {
            m_PieButtons = new List<UIButton>();
            this.m_Obj = obj;
            this.m_Caller = caller;
            this.m_Parent = parent;
            this.ButtonStyle = new TextStyle
            {
                Font = GameFacade.MainFont,
                Size = 12,
                Color = new Color(0xA5, 0xC3, 0xD6),
                SelectedColor = new Color(0x00, 0xFF, 0xFF),
                CursorColor = new Color(255, 255, 255)
            };

            m_Bg = new UIImage(TextureGenerator.GetPieBG(GameFacade.GraphicsDevice));
            m_Bg.SetSize(0, 0); //is scaled up later
            this.AddAt(0, m_Bg);

            m_PieTree = new UIPieMenuItem()
            {
                Category = true
            };

            for (int i = 0; i < pie.Count; i++)
            {
                string[] depth = pie[i].Name.Split('/');

                var category = m_PieTree; //set category to root
                for (int j = 0; j < depth.Length-1; j++) //iterate through categories
                {
                    if (category.Children.ContainsKey(depth[j]))
                    {
                        category = category.Children[depth[j]];
                    }
                    else
                    {
                        var newCat = new UIPieMenuItem()
                        {
                            Category = true,
                            Name = depth[j],
                            Parent = category
                        };
                        category.Children.Add(depth[j], newCat);
                        category = newCat;
                    }
                }
                //we are in the category, put the interaction in here;

                var item = new UIPieMenuItem()
                {
                    Category = false,
                    Name = depth[depth.Length-1],
                    ID = pie[i].ID
                };
                if (!category.Children.ContainsKey(item.Name)) category.Children.Add(item.Name, item);
            }

            m_CurrentItem = m_PieTree;
            m_PieButtons = new List<UIButton>();
            RenderMenu();

            VMAvatar Avatar = (VMAvatar)caller;
            m_Head = new SimAvatar(Avatar.Avatar); //talk about confusing...
            m_Head.StripAllButHead();

            initSimHead();
        }

        private void initSimHead()
        {
            HeadCamera = new BasicCamera(GameFacade.GraphicsDevice, new Vector3(0.0f, 7.0f, -17.0f), Vector3.Zero, Vector3.Up);

            HeadCamera.Position = new Vector3(0, 5.2f, 12.5f);
            HeadCamera.Target = new Vector3(0, 5.2f, 0.0f);

            HeadScene = new _3DScene(GameFacade.Game.GraphicsDevice, HeadCamera);
            HeadScene.ID = "UIPieMenuHead";

            //HeadCamera.NearPlane = 5;
            //HeadCamera.FarPlane = 923840284;

            //GameFacade.Game.GraphicsDevice.DeviceReset += new EventHandler(GraphicsDevice_DeviceReset);

            m_Head.Scene = HeadScene;
            m_Head.Scale = new Vector3(1f);
            HeadScene.Add(m_Head);
            GameFacade.Scenes.AddExternal(HeadScene); //AddExternal(HeadScene);
        }

        public void RotateHeadCam(Vector2 point)
        {
            double xdir = Math.Atan(-point.X / 100.0);
            double ydir = Math.Atan(-point.Y / 100.0);

            Vector3 off = new Vector3(0, 0, 13.5f);
            Matrix mat = Microsoft.Xna.Framework.Matrix.CreateRotationY((float)xdir) * Microsoft.Xna.Framework.Matrix.CreateRotationX((float)ydir);

            HeadCamera.Position = new Vector3(0, 5.2f, 0)+Vector3.Transform(off, mat);
        }

        public void RemoveSimScene()
        {
            GameFacade.Scenes.RemoveExternal(HeadScene);
        }

        public void UpdateHeadPosition(int x, int y)
        {
            HeadCamera.ProjectionOrigin = new Vector2(x, y);
        }

        public override void Update(TSO.Common.rendering.framework.model.UpdateState state)
        {
            base.Update(state);
            if (m_BgGrow < 1)
            {
                m_BgGrow += 1.0 / 30.0;
                HeadCamera.Zoom = (float)m_BgGrow;

                m_Bg.SetSize((float)m_BgGrow * 200, (float)m_BgGrow * 200);
                m_Bg.X = (float)m_BgGrow * (-100);
                m_Bg.Y = (float)m_BgGrow * (-100);
            }
            RotateHeadCam(GlobalPoint(new Vector2(state.MouseState.X, state.MouseState.Y)));
        }

        public void RenderMenu()
        {
            for (int i = 0; i < m_PieButtons.Count; i++) //remove previous buttons
            {
                this.Remove(m_PieButtons[i]);
            }
            m_PieButtons.Clear();

            var elems = m_CurrentItem.Children;
            int dirConfig;
            if (elems.Count > 4) dirConfig = 8;
            else if (elems.Count > 2) dirConfig = 4;
            else dirConfig = 2;

            for (int i = 0; i < dirConfig; i++)
            {
                if (i >= elems.Count) break;
                var elem = elems.ElementAt(i);
                var but = new UIButton()
                {
                    Caption = elem.Value.Name+((elem.Value.Category)?"...":""),
                    CaptionStyle = ButtonStyle,
                    ImageStates = 1,
                    Texture = TextureGenerator.GetPieButtonImg(GameFacade.GraphicsDevice)
                };

                double dir = (((double)i)/dirConfig)*Math.PI*2;
                but.AutoMargins = 4;

                if (i == 0) { //top
                    but.X = (float)(Math.Sin(dir)*60-but.Width/2);
                    but.Y = (float)((Math.Cos(dir)*-60)-but.Size.Y);
                } else if (i == dirConfig/2) { //bottom
                    but.X = (float)(Math.Sin(dir)*60-but.Width/2);
                    but.Y = (float)((Math.Cos(dir)*-60));
                }
                else if (i < dirConfig / 2) //on right side
                {
                    but.X = (float)(Math.Sin(dir) * 60);
                    but.Y = (float)((Math.Cos(dir) * -60) - but.Size.Y / 2);
                }
                else //on left side
                {
                    but.X = (float)(Math.Sin(dir) * 60-but.Width);
                    but.Y = (float)((Math.Cos(dir) * -60) - but.Size.Y / 2);
                }

                this.Add(but);
                m_PieButtons.Add(but);
                but.OnButtonClick += new ButtonClickDelegate(PieButtonClick);
                but.OnButtonHover += new ButtonClickDelegate(PieButtonHover);
            }

            bool top = true;
            for (int i = 8; i < elems.Count; i++)
            {
                var elem = elems.ElementAt(i);
                var but = new UIButton()
                {
                    Caption = elem.Value.Name+((elem.Value.Category)?"...":""),
                    CaptionStyle = ButtonStyle,
                    ImageStates = 1,
                    Texture = TextureGenerator.GetPieButtonImg(GameFacade.GraphicsDevice)
                };
                but.AutoMargins = 4;

                but.X = (float)(- but.Width / 2);
                if (top)
                { //top
                    but.Y = (float)(-60 - but.Size.Y*((i-8)/2 + 2));
                }
                else
                {
                    but.Y = (float)(60 + but.Size.Y * ((i - 8) / 2 + 1));
                }

                this.Add(but);
                m_PieButtons.Add(but);
                but.OnButtonClick += new ButtonClickDelegate(PieButtonClick);

                top = !top;
            }

            if (m_CurrentItem.Parent != null)
            {
                var but = new UIButton()
                {
                    Caption = m_CurrentItem.Name,
                    CaptionStyle = ButtonStyle.Clone(),
                    ImageStates = 1,
                    Texture = TextureGenerator.GetPieButtonImg(GameFacade.GraphicsDevice)
                };

                but.CaptionStyle.Color = but.CaptionStyle.SelectedColor;
                but.AutoMargins = 4;
                but.X = (float)(- but.Width / 2);
                but.Y = (float)(- but.Size.Y / 2);
                this.Add(but);
                m_PieButtons.Add(but);
                but.OnButtonClick += new ButtonClickDelegate(BackButtonPress);
            }
        }

        void PieButtonHover(UIElement button)
        {
            int index = m_PieButtons.IndexOf((UIButton)button);
            //todo, make sim look at button
            HITVM.Get().PlaySoundEvent(UISounds.PieMenuHighlight);
        }

        void BackButtonPress(UIElement button)
        {
            if (m_CurrentItem.Parent == null) return; //shouldn't ever be...
            m_CurrentItem = m_CurrentItem.Parent;
            HITVM.Get().PlaySoundEvent(UISounds.PieMenuSelect);
            RenderMenu();
        }

        private void PieButtonClick(UIElement button)
        {
            int index = m_PieButtons.IndexOf((UIButton)button);
            if (index == -1) return; //bail! this isn't meant to happen!
            var action = m_CurrentItem.Children.ElementAt(index).Value;
            HITVM.Get().PlaySoundEvent(UISounds.PieMenuSelect);

            if (action.Category) {
                m_CurrentItem = action;
                RenderMenu();
            } else {
                m_Obj.PushUserInteraction(action.ID, m_Caller, m_Parent.vm.Context);
                HITVM.Get().PlaySoundEvent(UISounds.QueueAdd);
                m_Parent.ClosePie();
                
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            base.Draw(batch);
            if (m_CurrentItem == m_PieTree)
            {
                //var oldd = GameFacade.GraphicsDevice.DepthStencilBuffer;
                //GameFacade.GraphicsDevice.DepthStencilBuffer = new DepthStencilBuffer(GameFacade.GraphicsDevice, oldd.Width, oldd.Height, oldd.Format);
                //todo: how to do this in xna4...
                GameFacade.GraphicsDevice.Clear(ClearOptions.DepthBuffer, new Vector4(0), 16777215, 0); //use a temp depth buffer for drawing this... this is an awful idea but will do until we get a better 3D UI element drawing system.
                batch.Pause();
                m_Head.Draw(GameFacade.GraphicsDevice);
                batch.Resume();
                //GameFacade.GraphicsDevice.DepthStencilBuffer = oldd;
            } //if we're top level, draw head!
        }
    }

    public class UIPieMenuItem
    {
        public bool Category;
        public byte ID;
        public string Name;
        public Dictionary<string, UIPieMenuItem> Children = new Dictionary<string, UIPieMenuItem>();
        public UIPieMenuItem Parent;
    }
}

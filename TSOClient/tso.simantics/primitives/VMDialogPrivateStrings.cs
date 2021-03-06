﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TSO.Simantics.engine;
using TSO.Files.utils;
using TSO.Files.formats.iff.chunks;

namespace TSO.Simantics.primitives
{
    public class VMDialogPrivateStrings : VMPrimitiveHandler
    {
        public override VMPrimitiveExitCode Execute(VMStackFrame context)
        {
            var operand = context.GetCurrentOperand<VMDialogStringsOperand>();
            VMDialogHandler.ShowDialog(context, operand, context.CodeOwner.Get<STR>(301));
            return VMPrimitiveExitCode.GOTO_TRUE;
        }
    }

    public class VMDialogStringsOperand : VMPrimitiveOperand
    {
        //engage and block sim, automatic icon, local reference 0, string not debug

        public byte Unknown1; //00
        public byte IconNameStringID;
        public byte MessageStringID;
        public byte YesStringID; //00 00
        public byte Unknown3;
        public byte Type;
        public byte TitleStringID;
        public byte Flags; 

        //Flags format:

        //                                             
        //   0           0           0           0           0           0           0           0
        //
        // Icon type:
        // 0 = auto,
        // 1 = none,
        // 2 = neighbour,
        // 3 = indexed,
        // 4 = named


        #region VMPrimitiveOperand Members
        public void Read(byte[] bytes)
        {
            using (var io = IoBuffer.FromBytes(bytes, ByteOrder.LITTLE_ENDIAN)){
                Unknown1 = io.ReadByte();
                if (Unknown1 != 0) throw new Exception("check this out");
                IconNameStringID = io.ReadByte();
                MessageStringID = io.ReadByte();
                YesStringID = io.ReadByte();
                if (YesStringID != 0) throw new Exception("check this out");
                Unknown3 = io.ReadByte();
                if (Unknown3 != 0) throw new Exception("check this out");
                Type = io.ReadByte();
                TitleStringID = io.ReadByte();
                Flags = io.ReadByte();
            }
        }
        #endregion
    }
}

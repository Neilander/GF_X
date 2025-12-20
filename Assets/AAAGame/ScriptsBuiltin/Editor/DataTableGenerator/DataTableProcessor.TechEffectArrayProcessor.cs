//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2020 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System.IO;

namespace GameFramework.Editor.DataTableTools
{
    public sealed partial class DataTableProcessor
    {
        private sealed class TechEffectArrayProcessor : GenericDataProcessor<TechEffect[]>
        {
            public override bool IsSystem => false;

            public override string LanguageKeyword => "TechEffect[]";

            public override int ShowOrder => 96;

            public override string[] GetTypeStrings()
            {
                return new string[]
                {
                    "techeffect[]",
                };
            }

            public override TechEffect[] Parse(string value)
            {
                return DataTableExtension.ParseTechEffectArray(value);
            }

            public override void WriteToStream(DataTableProcessor dataTableProcessor, BinaryWriter binaryWriter, string value)
            {
                var v = Parse(value);
                if (v == null)
                {
                    binaryWriter.Write7BitEncodedInt32(0);
                    return;
                }

                binaryWriter.Write7BitEncodedInt32(v.Length);
                for (int i = 0; i < v.Length; i++)
                {
                    var itm = v[i];
                    binaryWriter.Write7BitEncodedInt32((int)itm.Type);
                    binaryWriter.Write(SerializeArgs(itm));
                }
            }

            private static string SerializeArgs(TechEffect itm)
            {
                switch (itm.Type)
                {
                    case TechEffectType.GrantCapability:
                    case TechEffectType.GrantRecipe:
                    case TechEffectType.UnlockBuildable:
                    case TechEffectType.EnableFeature:
                        return itm.S1 ?? string.Empty;
                    default:
                        return string.Empty;
                }
            }
        }
    }
}

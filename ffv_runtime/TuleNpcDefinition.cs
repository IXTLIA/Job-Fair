// Technical instruction identity of an existing, non-service Tule townsperson.
namespace ProjectBlues.Runtime
{
    public static class TuleNpcDefinition
    {
        public const string ShortLocation = "green-haired man beside the pub in Tule";
        public const string SourceAssetPath = "Assets/GameAssets/Serial/Res/Map/Map_20010/Map_20010/sc_npc_20010_1.json";
        public const string SourceSha256 = "0f934c1a96671d3c97f31b2a9b711a55b55bc3f5e145081e7e4a248eb8476a41";
        private static readonly OpeningScript definition = new OpeningScript(TechnicalJson);
        public static OpeningScript Script => definition;

        // Source entity: ev_e_0000 object141/entity24, scriptID1500,
        // asset48, fixed Tiled position464/656. Original dialogue is untouched.
        // This subset omits cosmetic comments/title and English game prose;
        // runtime identity compares every native label/opcode/type/operand.
        private const string TechnicalJson = @"{""Mnemonics"":[
          {""label"":""Main"",""mnemonic"":""Nop"",""type"":1,""operands"":{""iValues"":[],""rValues"":[],""sValues"":[]}},
          {""label"":"""",""mnemonic"":""Branch"",""type"":1,""operands"":{""iValues"":[22,1,4,0,0,0,0,0],""rValues"":[0,0,0,0,0,0,0,0],""sValues"":[""ScenarioFlag1"",""＝"",""imm"","""","""","""","""",""""]}},
          {""label"":"""",""mnemonic"":""Msg"",""type"":1,""operands"":{""iValues"":[0,0,0,0,0,0,0,0],""rValues"":[0,0,0,0,0,0,0,0],""sValues"":[""N102_C01_030_05_01"","""","""","""","""","""","""",""""]}},
          {""label"":"""",""mnemonic"":""Exit"",""type"":1,""operands"":{""iValues"":[0,0,0,0,0,0,0,0],""rValues"":[0,0,0,0,0,0,0,0],""sValues"":["""","""","""","""","""","""","""",""""]}},
          {""label"":""e_22 後"",""mnemonic"":""Nop"",""type"":1,""operands"":{""iValues"":[],""rValues"":[],""sValues"":[]}},
          {""label"":"""",""mnemonic"":""Msg"",""type"":1,""operands"":{""iValues"":[0,0,0,0,0,0,0,0],""rValues"":[0,0,0,0,0,0,0,0],""sValues"":[""N102_C01_030_05_02"","""","""","""","""","""","""",""""]}},
          {""label"":"""",""mnemonic"":""Exit"",""type"":1,""operands"":{""iValues"":[0,0,0,0,0,0,0,0],""rValues"":[0,0,0,0,0,0,0,0],""sValues"":["""","""","""","""","""","""","""",""""]}}
        ]}";
    }
}

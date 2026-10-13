using System;
using System.Collections.Generic;
using System.Linq;
namespace ThirdPersonGameplay.Configuration { public static class GameplayConfigFields {
static readonly IReadOnlyList<GameplayConfigFieldDescription> s_Attributes = Array.AsReadOnly(new GameplayConfigFieldDescription[] {
new GameplayConfigFieldDescription("4ab2787a2957fd653fb3f890022f9389e0534215b3564051f4063b914f2df176", "a806be91a5899341178752c99f299e25e6c29eac2e5dbda7ab4ffe907c54122d", "Id", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("fe273ad5a629fcbc266162f6a2b0c95daadcd3ed8283bb9004eff08348c81c37", "2f2a3dae5d5259a2c5a5faa8060a0d51d77a35b32cd4c18898fca5b46bf37037", "AttributeId", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("389866ec5cf9aa36ddb31d7d72b30caa484718b93c4c21014c368340d63d6071", "2a9e902e4fd4283f0e3bab014551d1b0f1a1d9f7a5d9c41e9db54e5814c71dac", "Minimum.Enabled", "bool", GameplayConfigValueKind.Boolean),
new GameplayConfigFieldDescription("8640809ce744a544a4bb657fd216959eec0901cb1385ef6d53ab9008b7f89fd3", "7395b48dfe67a372d93d97ba2cb68df0b4d479c6446b5d469e136e5ff8dc3536", "Minimum.Source", "BoundSource", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("5050cb0729ebdb3b7ea3ca30fc7ef92ed03864b1ab93f10b2f6d92095b6cb45c", "505539937e5350ef00b1d71e4149dc095556e88559efdfc4b9f4c267e004b7df", "Minimum.Constant", "float", GameplayConfigValueKind.Number),
new GameplayConfigFieldDescription("7ea7753a9261aeb233e044ffd9d415ee9e679b66c981beb03b5329dfa709dabd", "5d44df58783bcf297e0b5aa3cf38e1b618aa41d8583642e2c620238b3d3dc5c4", "Minimum.AttributeId", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("7a6718a63cf51cf07dd2a81d712047b75434de0248c538abd4f0141094f2e27f", "9bb7eff4f7cf42d286f37c4a70db836459ca4dab24b87551d90500104d4ccc0a", "Maximum.Enabled", "bool", GameplayConfigValueKind.Boolean),
new GameplayConfigFieldDescription("71c6746838ae7b6e1d3fe2797f7f7eb5e92dd97921c8d615fcc6a039fa7af55d", "18889c1a21417e9b804e3a7a33468edaf3ce25c08cd07eaabc13af22c7c22e18", "Maximum.Source", "BoundSource", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("683a6ddfb465d6eaa43debb0b171cf52aa8b331c9cd3f82df5186ff76b806b4b", "e325970f55477f97d9a619f342791aa8986938c86bcba85fed4626d573cd80d8", "Maximum.Constant", "float", GameplayConfigValueKind.Number),
new GameplayConfigFieldDescription("e9adc1b2d495a6c994a466363a9c11863d5278298ac34ec66b582b797686f0df", "ed6854fc572a5ec93e3f9a587b37776b9d0d9418147a09be5472d77f1d5f48f9", "Maximum.AttributeId", "string", GameplayConfigValueKind.Text),
});
static readonly IReadOnlyList<GameplayConfigFieldDescription> s_Effects = Array.AsReadOnly(new GameplayConfigFieldDescription[] {
new GameplayConfigFieldDescription("969704024477390ad6875b93215f91001e1da6f3de1bcd60615d013018131cea", "9824989b668294422a9c7fddb842f5044e625ebc6d7093720553352be4c9509e", "Id", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("52d57c40d27a5280c8432738e5e3de0474663d07fd0ccd672066c922d1db8c0f", "27ef49854f6b49905860223cf85b55d13cc28a2437fc53f46e08ccfc05e35560", "EffectId", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("cc456f3a61412568ed4684634685caf6da7094980c6c264ee0e450d6fee27c9a", "a43c8b8443f35bd0c682d31bc010c337f6ef55ad94aa19841be5ff485dd3158d", "DefinitionRevision", "uint", GameplayConfigValueKind.UInt64),
new GameplayConfigFieldDescription("2bb4c3a3951a3214de1caca4529f5c297a9423d53fdcbaa36a7c62b361a3fded", "1c9d242d63b42c19b5a72f5f7b8c99e8b6c32d861b29c3e781a0e766351a53bf", "ScopePolicy", "ScopePolicy", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("f941acadb65e9268d3e7c8f99b9722fdecb64e536a6deeb5707572acd0fce427", "81cf6a5ed1fa5dce2202a6bcad03679bd91db7ec77eb016214e76f4abc97824b", "LifetimePolicy", "LifetimePolicy", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("c598ef461545031f5f36bbd0fa32e3ae1ea905ec08241c423d54228be6f630a2", "380e26df0fc3d470f67b43aaad175b39e67c298510da43f86081bdcf10ca597d", "EffectTags", "array,string", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("3aa12fd9502c272794c9515a5e04848718fc0dc6346a94ecc273db8f1230ccaf", "e7aa085727c924d86faeaca376559f3bb98da25dcba6f6d933f3776a9a07ea64", "DurationPolicy", "DurationPolicy", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("42a826a742bf06abb06f2272517995d6cb5c286391e9f73ad3c69efd22a91322", "b63d2d87ab0d1c9501147fd18dc67ae12093bc7228196b99274c9ae33053c8cc", "DurationMagnitude", "Magnitude", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("251930650d960c5b9fdb839f972aeb431196fb2c27b5a24c80517ec6ec842d44", "bcb98816dff1c23124eb7f9f1e458b5dffa050a7169648f20e7aaaaf51717671", "HasPeriod", "bool", GameplayConfigValueKind.Boolean),
new GameplayConfigFieldDescription("9913fb3fa1e26c92f10f2a08bcc9af0304df8b523dc6ef224411ac5d94a545f8", "635a3e763e9428385f609cd4eec695aeb2ac137cd4ae78191e2c216c4daf750a", "PeriodMagnitude", "Magnitude", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("25660ad8098409fe4ca96d4fd12c3cd24aef0dee58a0d02a216055e7471b2d84", "842d7a068e8e9db7d58154d49692761e0db240733bfc7e6ba972a3b292c9397a", "ExecuteOnApplication", "bool", GameplayConfigValueKind.Boolean),
new GameplayConfigFieldDescription("cf976f5699e75aecf02e6d3c1bb9943a234f45fef4d5a4fb7e9cd8dbd8bf078d", "21397fe084450bae27a0a6a47141520a247254602b50afd81b7baa63d8603b61", "StackingPolicy", "StackingPolicy", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("5f0b4761228257cb1eae7cf51dbfa8e678e95522ca75a725ef843bcb74ee7743", "ec9499ca212959125ceb59996bad95afe62c8f7d6689c33bf7ed8e87c9446711", "MaxStacks", "int", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("24778a7f742321edaea1ed695b78edca2dd494a6fe492b03d6b4ac60f8d8ed48", "96ace444ee24c4269073e5d67c606d218b6b0d036f0599e4834a09d7ab86860b", "DurationUpdate", "DurationUpdate", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("6cafccca24605a7b9d84756451ed418eb9fbc1dc52ce25ac830a1e550e5809ab", "84fd9cfd2f08035076858a4ae5ad72e38830c3e709af991d92378193cd615478", "PeriodUpdate", "PeriodUpdate", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("1ba7e76387ecf72872295159ea9924392b39eb275bdab375b7039d95735bbb76", "1ec1663337ae89f991b9ebcf6a1a0a72be37a254a4a74ab28b48eaea7c72ccd3", "OverflowPolicy", "OverflowPolicy", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("bdf69a582ac1e6cb3618a72534d139666d1e3788d225ad7fdf9c9f4f081e8006", "36b8ad5bfb081d1d2c563ef6903ed464cc40aa993dd7905c246dacf47a7e25dd", "SetByCallerParameters", "array,string", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("8104341f7b3d4d88b5145faa76960172b459bfec7d26307f164491af7b2d82bd", "c7978c90f931c3f058304ee74e57deceebbacd56b6343f4a128dc3429d8ceff8", "Components", "array,string", GameplayConfigValueKind.Unsupported),
});
static readonly IReadOnlyList<GameplayConfigFieldDescription> s_Components = Array.AsReadOnly(new GameplayConfigFieldDescription[] {
new GameplayConfigFieldDescription("3105b303a7a48ab09e49232fa4331673fd8643c19ad79369709bc05078e429ec", "5c1e2f182b9e282edfecabc69925ba86033f4703a62ea0a3b4a8e22d0bc5a96c", "Id", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("87c93a260fd8c9e105dec50dca61c60137b0521e52e07405bbb83477d83d0123", "bbeeb6aa241ddfe9a0e57d17589f3356d4ad0fa5763aeb4a3d71f761e0b2c50f", "Value", "Component", GameplayConfigValueKind.Unsupported),
});
static readonly IReadOnlyList<GameplayConfigFieldDescription> s_Mutations = Array.AsReadOnly(new GameplayConfigFieldDescription[] {
new GameplayConfigFieldDescription("06cc834a2cb2a631bb35c447fefcd1443b8bd0ca1b0a8a4b15e0713fd4e8fc40", "1e79750bbe87d6dd617f337e4c71e80ded0207ee4af835e0671143a1d5763db8", "Id", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("12c0f83576221e1a76efcddc1a77939450884216d4d4ef5d9d96e18bc2312ef1", "1571c643cd761d137d0a94477b35a98f3e7a02697fde1f8fcc742127167f61b0", "AttributeId", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("5d5317e6fda6cbd8b8c004992e834541a37cd8d4c7270ecf824816a43b609a18", "50c8f6e3a9e72d4c54d97bd194370231fedadba10681246acd91fbd4d29431f5", "Operation", "ModifierOperation", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("b98e5bdc633dc4356ccaf4dd298b8bca4a9c272480bc9f44ea1539b6117b702e", "cd02e514ac884a71466ff7aa7343341d292ccd0588f2d37e251fecd86c28b9dc", "Magnitude", "Magnitude", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("1eacf555aaf4ae1c2ea94de348f42f8f9b6364911bf68aff185aa1b6820ffbfc", "de7e4016d174ccb3d52715aab900a5b9136744bcd8ad234d5787d71a621ab85a", "ClampBound", "ClampBound", GameplayConfigValueKind.Int32),
});
static readonly IReadOnlyList<GameplayConfigFieldDescription> s_AdditionalEffects = Array.AsReadOnly(new GameplayConfigFieldDescription[] {
new GameplayConfigFieldDescription("d0d026a41c218146e47a5fc59f79ec3a1ce04eb1ae30df1b29f19dd7db825152", "fec3f61b8168b8ef097308477874c605c377153902fec5308fe8a48f072b8160", "Id", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("47f288c723e75c272e7b74cf485d56aab01ac902642d8292199c952859f0aa6a", "633a687bcc0b32784fe7ebadbeda790fc32d144c7dd1c29949041a6a1d19dbd7", "Trigger", "AdditionalTrigger", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("32df59f56ed3af35417066fffd04ce0639d2aefc2205ee22967e4786f63cd90a", "251902c948ca238f0cf6a55088dae967a762af5426b82fc2bbe05597afe966cf", "Effect", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("7518def27af6d549b2b163eea1f0fbd2a462171836343d7264e14bb3c5526a36", "eb2931e1e7061f8890dc6b1072769fda2822e04e1dc6ce625934d369d2d44220", "ParameterBindings", "array,string", GameplayConfigValueKind.Unsupported),
});
static readonly IReadOnlyList<GameplayConfigFieldDescription> s_ParameterBindings = Array.AsReadOnly(new GameplayConfigFieldDescription[] {
new GameplayConfigFieldDescription("312fe80cc59b71aed0ba8f5ed518f3f9ce728e08b319c1827ab64f6b6abf1bfb", "4c124bab3ed7acd2f99beae75ebb48195ce1791be6ed30c6c5960a16a7bc1355", "Id", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("b619f5d6dd0bbd485b95a42e0c1474c4e77171fcf504451873a514b652287912", "f5f69b1b601ac1846349a22fb31625a4dfba06c5c79f6db11ffe7473ae0c4728", "ChildParameterId", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("ad8dff4d702aa1fdbff8e953251ccf5d7770b8c9f3a8e527c0b58fc1b001da9f", "e9654a86b48ed8f2fc3eeb7112704aa17ad04f38e1e878477e4150c7bde650e0", "Source", "ParameterSource", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("cd1bb99e3cc88ecf77fc45b5bd3f3ca0bafe8269e8d848bb6bc3707810a4cad0", "c6bd0902b700ae687f7d0b5cc80fb3b73128eeb21aa99cd1ed084c81b1ff034b", "ParentParameterId", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("1e221a7046c1192bd38cd6e7c780df42f4200b396c4c0748752da04a782f1f02", "cfa68ffadad095b5cadeeb8ad26e73cd647782ad0c36c4498488494a55dcf98d", "Constant", "float", GameplayConfigValueKind.Number),
});
static readonly IReadOnlyList<GameplayConfigFieldDescription> s_InitialAttributes = Array.AsReadOnly(new GameplayConfigFieldDescription[] {
new GameplayConfigFieldDescription("377f4a978626ac992c7c5d586bf88bdfb669491b500f812c9adb42c041cbc768", "2db0362859ac1e3cb9a911edb1d4bc52aa7c13821705c80c7a85dc7093028c69", "Id", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("dd022e5c6841ecfc885e9d9ced07e79c75c462f97f7b1c2c3ed15ceab955ae91", "95f72eeb2a587f1de7622a7c7c459137c0b1e9d2576ef36eb83092cffc4dceab", "Attribute", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("6a67b8550a6713b9a1e3d06a30f7467c706c0f76390bafdc0ba53ed9639ebeac", "02dadbb2d403e28c48812d8f1c9e0c5af1cef1ce96966f97e71d8f49977778ca", "BaseValue", "float", GameplayConfigValueKind.Number),
});
static readonly IReadOnlyList<GameplayConfigFieldDescription> s_InitialEffects = Array.AsReadOnly(new GameplayConfigFieldDescription[] {
new GameplayConfigFieldDescription("9179a5e72ccd0fbb76534469a0d7f2a2a19611ff2133d68ddfc59f63e175b712", "16fd1553991388ee4ffd2b053f938f2251b4e11dda177cc7165e456584ba6145", "Id", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("9e04213d1d0e745aa01c2622131b2ef9901dcbd077cb494555242c2ce56e7a49", "b468cc7b8d3bb28c0c0651a38e9ce8053a0dfb925e8e2e29aff4f4886e00cd9a", "Effect", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("7f7fc63452b1998e4b116bd89d6682cb7c76eb43c327b1a63035cf2de4e872ad", "a37437ac76a1245a19c1e8432389bc899febc39ac7f4eccf2db67debcdcc56c3", "Parameters", "array,string", GameplayConfigValueKind.Unsupported),
});
static readonly IReadOnlyList<GameplayConfigFieldDescription> s_InitialParameters = Array.AsReadOnly(new GameplayConfigFieldDescription[] {
new GameplayConfigFieldDescription("fbbe634a693779e3b5fc9a92148b6ed2c46d748c210b616f76e7c4f6f20be91a", "f59ed8cebb8ddc9bc821e5306726f47a4db3af1360846dc7f8799f07a85c1e33", "Id", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("f9a8287d070e2024a023ae0485484a1f9ab9b8ae22f35db7d26105f16163125a", "0d63977072fa7f7bd8b6d43a47945c0d689d9c627b18cd1747ff5ff9cfae2ead", "ParameterId", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("7a6112397c7be885b6dbc697513b1f4ed047f05610ecd5f9e39a8c07761fb443", "084a58de377d749ccef90b22932c59115590d6679f6a1b70b501e2e48ce3d408", "Value", "float", GameplayConfigValueKind.Number),
});
static readonly IReadOnlyList<GameplayConfigFieldDescription> s_Characters = Array.AsReadOnly(new GameplayConfigFieldDescription[] {
new GameplayConfigFieldDescription("19bc880486413a05498566725bbe35724a75286f8c000a83810bbf26cc618899", "9dffca356aef45bde5ff2e1cac236631aa16fb1d5d9d41edbc7a3ebb929afd0a", "Id", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("9cc7ccb9fee6dad5d5d6adbede625d8b5cfd487a303b11bb1d7bc79d278f6672", "ca60fdb3e55286c1b7151a3ae2da39a551e1a4a4790c2db40fe08d1323f83f63", "Attributes", "array,string", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("b9f6ea474fc417b09e85e8ffbaf3252c475c2aaaac07a4fbfec902b1a895fe56", "e25b39222c75a5043917d826a0f13e0482eb564a956b450321e4f96e71cc7ba6", "InitialAttributes", "array,string", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("80db5eab739680fdf1617dc32cb3bc4e9f18bccd228ccd5371d1db6a85fede94", "eba6c1f86f5117831c94c964622c6b4c7ca7afcf9fac1576cd396da5a5e7ce20", "InitialTags", "array,string", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("eab12776ab905a8b4bf4ee0eb0b9dd46d3e7d8b40d8e3326972b74f377c70b29", "86474526feaabe58a51f2a450790f6e7d23c73cfad2cd513baca16c521800fa2", "Effects", "array,string", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("23c39309645f0d786e5126ff40ffbcbb4f6d6f949a120ac1dbfd7d135e49da3c", "5e43e4af941cfcb75de2d64b5bba4075a87061d9a89d4ffa7439bfe73cf9818f", "InitialEffects", "array,string", GameplayConfigValueKind.Unsupported),
});
public static IReadOnlyList<GameplayConfigFieldDescription> Describe(GameplayConfigRecordKind kind) => kind switch {
GameplayConfigRecordKind.Attributes => s_Attributes,
GameplayConfigRecordKind.Effects => s_Effects,
GameplayConfigRecordKind.Components => s_Components,
GameplayConfigRecordKind.Mutations => s_Mutations,
GameplayConfigRecordKind.AdditionalEffects => s_AdditionalEffects,
GameplayConfigRecordKind.ParameterBindings => s_ParameterBindings,
GameplayConfigRecordKind.InitialAttributes => s_InitialAttributes,
GameplayConfigRecordKind.InitialEffects => s_InitialEffects,
GameplayConfigRecordKind.InitialParameters => s_InitialParameters,
GameplayConfigRecordKind.Characters => s_Characters,
_ => throw new ArgumentOutOfRangeException(nameof(kind)) }; 
public static GameplayConfigRecordKind RecordKind(string table) => table switch {
"Attributes" => GameplayConfigRecordKind.Attributes,
"Effects" => GameplayConfigRecordKind.Effects,
"Components" => GameplayConfigRecordKind.Components,
"Mutations" => GameplayConfigRecordKind.Mutations,
"AdditionalEffects" => GameplayConfigRecordKind.AdditionalEffects,
"ParameterBindings" => GameplayConfigRecordKind.ParameterBindings,
"InitialAttributes" => GameplayConfigRecordKind.InitialAttributes,
"InitialEffects" => GameplayConfigRecordKind.InitialEffects,
"InitialParameters" => GameplayConfigRecordKind.InitialParameters,
"Characters" => GameplayConfigRecordKind.Characters,
_ => throw new InvalidOperationException("配置表不属于已编译 schema：" + table) }; 
public static GameplayConfigValue Read(PublishedGameplayConfiguration tables, GameplayConfigRecordKind kind, string recordId, string fieldId) { switch (kind) {
case GameplayConfigRecordKind.Attributes: {
var value = tables.Attributes.Single(item => item.Id == recordId); switch (fieldId) {
case "4ab2787a2957fd653fb3f890022f9389e0534215b3564051f4063b914f2df176": return GameplayConfigValue.FromText(value.Id);
case "fe273ad5a629fcbc266162f6a2b0c95daadcd3ed8283bb9004eff08348c81c37": return GameplayConfigValue.FromText(value.AttributeId);
case "389866ec5cf9aa36ddb31d7d72b30caa484718b93c4c21014c368340d63d6071": return GameplayConfigValue.FromBoolean(value.Minimum.Enabled);
case "8640809ce744a544a4bb657fd216959eec0901cb1385ef6d53ab9008b7f89fd3": return GameplayConfigValue.FromInt32((int)value.Minimum.Source);
case "5050cb0729ebdb3b7ea3ca30fc7ef92ed03864b1ab93f10b2f6d92095b6cb45c": return GameplayConfigValue.FromNumber(value.Minimum.Constant);
case "7ea7753a9261aeb233e044ffd9d415ee9e679b66c981beb03b5329dfa709dabd": return GameplayConfigValue.FromText(value.Minimum.AttributeId);
case "7a6718a63cf51cf07dd2a81d712047b75434de0248c538abd4f0141094f2e27f": return GameplayConfigValue.FromBoolean(value.Maximum.Enabled);
case "71c6746838ae7b6e1d3fe2797f7f7eb5e92dd97921c8d615fcc6a039fa7af55d": return GameplayConfigValue.FromInt32((int)value.Maximum.Source);
case "683a6ddfb465d6eaa43debb0b171cf52aa8b331c9cd3f82df5186ff76b806b4b": return GameplayConfigValue.FromNumber(value.Maximum.Constant);
case "e9adc1b2d495a6c994a466363a9c11863d5278298ac34ec66b582b797686f0df": return GameplayConfigValue.FromText(value.Maximum.AttributeId);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
case GameplayConfigRecordKind.Effects: {
var value = tables.Effects.Single(item => item.Id == recordId); switch (fieldId) {
case "969704024477390ad6875b93215f91001e1da6f3de1bcd60615d013018131cea": return GameplayConfigValue.FromText(value.Id);
case "52d57c40d27a5280c8432738e5e3de0474663d07fd0ccd672066c922d1db8c0f": return GameplayConfigValue.FromText(value.EffectId);
case "cc456f3a61412568ed4684634685caf6da7094980c6c264ee0e450d6fee27c9a": return GameplayConfigValue.FromUInt64(value.DefinitionRevision);
case "2bb4c3a3951a3214de1caca4529f5c297a9423d53fdcbaa36a7c62b361a3fded": return GameplayConfigValue.FromInt32((int)value.ScopePolicy);
case "f941acadb65e9268d3e7c8f99b9722fdecb64e536a6deeb5707572acd0fce427": return GameplayConfigValue.FromInt32((int)value.LifetimePolicy);
case "3aa12fd9502c272794c9515a5e04848718fc0dc6346a94ecc273db8f1230ccaf": return GameplayConfigValue.FromInt32((int)value.DurationPolicy);
case "251930650d960c5b9fdb839f972aeb431196fb2c27b5a24c80517ec6ec842d44": return GameplayConfigValue.FromBoolean(value.HasPeriod);
case "25660ad8098409fe4ca96d4fd12c3cd24aef0dee58a0d02a216055e7471b2d84": return GameplayConfigValue.FromBoolean(value.ExecuteOnApplication);
case "cf976f5699e75aecf02e6d3c1bb9943a234f45fef4d5a4fb7e9cd8dbd8bf078d": return GameplayConfigValue.FromInt32((int)value.StackingPolicy);
case "5f0b4761228257cb1eae7cf51dbfa8e678e95522ca75a725ef843bcb74ee7743": return GameplayConfigValue.FromInt32(value.MaxStacks);
case "24778a7f742321edaea1ed695b78edca2dd494a6fe492b03d6b4ac60f8d8ed48": return GameplayConfigValue.FromInt32((int)value.DurationUpdate);
case "6cafccca24605a7b9d84756451ed418eb9fbc1dc52ce25ac830a1e550e5809ab": return GameplayConfigValue.FromInt32((int)value.PeriodUpdate);
case "1ba7e76387ecf72872295159ea9924392b39eb275bdab375b7039d95735bbb76": return GameplayConfigValue.FromInt32((int)value.OverflowPolicy);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
case GameplayConfigRecordKind.Components: {
var value = tables.Components.Single(item => item.Id == recordId); switch (fieldId) {
case "3105b303a7a48ab09e49232fa4331673fd8643c19ad79369709bc05078e429ec": return GameplayConfigValue.FromText(value.Id);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
case GameplayConfigRecordKind.Mutations: {
var value = tables.Mutations.Single(item => item.Id == recordId); switch (fieldId) {
case "06cc834a2cb2a631bb35c447fefcd1443b8bd0ca1b0a8a4b15e0713fd4e8fc40": return GameplayConfigValue.FromText(value.Id);
case "12c0f83576221e1a76efcddc1a77939450884216d4d4ef5d9d96e18bc2312ef1": return GameplayConfigValue.FromText(value.AttributeId);
case "5d5317e6fda6cbd8b8c004992e834541a37cd8d4c7270ecf824816a43b609a18": return GameplayConfigValue.FromInt32((int)value.Operation);
case "1eacf555aaf4ae1c2ea94de348f42f8f9b6364911bf68aff185aa1b6820ffbfc": return GameplayConfigValue.FromInt32((int)value.ClampBound);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
case GameplayConfigRecordKind.AdditionalEffects: {
var value = tables.AdditionalEffects.Single(item => item.Id == recordId); switch (fieldId) {
case "d0d026a41c218146e47a5fc59f79ec3a1ce04eb1ae30df1b29f19dd7db825152": return GameplayConfigValue.FromText(value.Id);
case "47f288c723e75c272e7b74cf485d56aab01ac902642d8292199c952859f0aa6a": return GameplayConfigValue.FromInt32((int)value.Trigger);
case "32df59f56ed3af35417066fffd04ce0639d2aefc2205ee22967e4786f63cd90a": return GameplayConfigValue.FromText(value.Effect);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
case GameplayConfigRecordKind.ParameterBindings: {
var value = tables.ParameterBindings.Single(item => item.Id == recordId); switch (fieldId) {
case "312fe80cc59b71aed0ba8f5ed518f3f9ce728e08b319c1827ab64f6b6abf1bfb": return GameplayConfigValue.FromText(value.Id);
case "b619f5d6dd0bbd485b95a42e0c1474c4e77171fcf504451873a514b652287912": return GameplayConfigValue.FromText(value.ChildParameterId);
case "ad8dff4d702aa1fdbff8e953251ccf5d7770b8c9f3a8e527c0b58fc1b001da9f": return GameplayConfigValue.FromInt32((int)value.Source);
case "cd1bb99e3cc88ecf77fc45b5bd3f3ca0bafe8269e8d848bb6bc3707810a4cad0": return GameplayConfigValue.FromText(value.ParentParameterId);
case "1e221a7046c1192bd38cd6e7c780df42f4200b396c4c0748752da04a782f1f02": return GameplayConfigValue.FromNumber(value.Constant);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
case GameplayConfigRecordKind.InitialAttributes: {
var value = tables.InitialAttributes.Single(item => item.Id == recordId); switch (fieldId) {
case "377f4a978626ac992c7c5d586bf88bdfb669491b500f812c9adb42c041cbc768": return GameplayConfigValue.FromText(value.Id);
case "dd022e5c6841ecfc885e9d9ced07e79c75c462f97f7b1c2c3ed15ceab955ae91": return GameplayConfigValue.FromText(value.Attribute);
case "6a67b8550a6713b9a1e3d06a30f7467c706c0f76390bafdc0ba53ed9639ebeac": return GameplayConfigValue.FromNumber(value.BaseValue);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
case GameplayConfigRecordKind.InitialEffects: {
var value = tables.InitialEffects.Single(item => item.Id == recordId); switch (fieldId) {
case "9179a5e72ccd0fbb76534469a0d7f2a2a19611ff2133d68ddfc59f63e175b712": return GameplayConfigValue.FromText(value.Id);
case "9e04213d1d0e745aa01c2622131b2ef9901dcbd077cb494555242c2ce56e7a49": return GameplayConfigValue.FromText(value.Effect);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
case GameplayConfigRecordKind.InitialParameters: {
var value = tables.InitialParameters.Single(item => item.Id == recordId); switch (fieldId) {
case "fbbe634a693779e3b5fc9a92148b6ed2c46d748c210b616f76e7c4f6f20be91a": return GameplayConfigValue.FromText(value.Id);
case "f9a8287d070e2024a023ae0485484a1f9ab9b8ae22f35db7d26105f16163125a": return GameplayConfigValue.FromText(value.ParameterId);
case "7a6112397c7be885b6dbc697513b1f4ed047f05610ecd5f9e39a8c07761fb443": return GameplayConfigValue.FromNumber(value.Value);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
case GameplayConfigRecordKind.Characters: {
var value = tables.Characters.Single(item => item.Id == recordId); switch (fieldId) {
case "19bc880486413a05498566725bbe35724a75286f8c000a83810bbf26cc618899": return GameplayConfigValue.FromText(value.Id);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
default: throw new ArgumentOutOfRangeException(nameof(kind)); } } } }

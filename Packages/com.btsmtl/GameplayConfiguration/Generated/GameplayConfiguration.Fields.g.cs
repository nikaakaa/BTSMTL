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
new GameplayConfigFieldDescription("60f0ac683719d2e7cadc25d66e2ff5de65928ac4c661393745b682cd031dfa3a", "b69c3691f0ea18a633c20819268eef27b8b76cc5f3dc2361a3f3efe31ecdcc7c", "Duration.Policy", "DurationPolicy", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("15a69370d1a79153b641b8600a2a85ea207efa303f5d4b51cea04bcefc61f0e6", "e1e62fbb13f9cdeaaeeff504446316d953645a0f33d4dd2eeeb6a20ee253f885", "Duration.Magnitude", "Magnitude", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("fa187d1262a4fa9d3307cba0f07cb3e6cfbe6072a946a9e43ab7607be4955f1a", "6d6e628f19d7fba7ca814e61136c811a320f44df29e9f4c98ddd0fe9b7d59b8f", "Period.Enabled", "bool", GameplayConfigValueKind.Boolean),
new GameplayConfigFieldDescription("fa470fed3eaba20b8615d9abdbc93a345b814b6a5c80adcac870a5010a44f0b1", "eecba763fc3ef8bd0b45e4ac4f8d2427b6eb6f421b46e5c04caa9e265593090d", "Period.Magnitude", "Magnitude", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("25660ad8098409fe4ca96d4fd12c3cd24aef0dee58a0d02a216055e7471b2d84", "842d7a068e8e9db7d58154d49692761e0db240733bfc7e6ba972a3b292c9397a", "ExecuteOnApplication", "bool", GameplayConfigValueKind.Boolean),
new GameplayConfigFieldDescription("e832c834fd3abc22eb47f6ab685244bf1d0a09d89a37aa52dfac1d99b19b26eb", "c3f3a8a7560de55966f2148e69db7d3810fbeea178195412144e859979934776", "Stacking.Policy", "StackingPolicy", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("fc32eac806c04a1d352a6c1d202efa35e44a2714d8642a7a06c35e6ee082274b", "b7eac46463bcb79f91790f32f04a8d843be5795912cf4e928b11af8657cd30e8", "Stacking.MaxStacks", "int", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("65260c368f9bc3e20224176ad1dbdfae055da39963b108a6be9902e84ecaa609", "39ddb67c082e40c2011c9508b08fc2fd3a241f973c3bdb8f7348ab44841128a2", "Stacking.DurationUpdate", "DurationUpdate", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("84d305224d9fefd192ae3a1075123db6e84ac47cd3b4581b29b14cc28ac46504", "cf7731b0ca632f2e9ca87a8d64f4ef0080d1f1258e19a21da2bd5214fefcff1d", "Stacking.PeriodUpdate", "PeriodUpdate", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("1ee695c82bee907a33c4e86748d815c8edc03b63c7dfa5fe9a251581f27f6ed7", "d9e6389070544caa947672af366cdf9ba930c807b0e7994a8c7dab525e62bfbd", "Stacking.OverflowPolicy", "OverflowPolicy", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("bdf69a582ac1e6cb3618a72534d139666d1e3788d225ad7fdf9c9f4f081e8006", "36b8ad5bfb081d1d2c563ef6903ed464cc40aa993dd7905c246dacf47a7e25dd", "SetByCallerParameters", "array,string", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("8104341f7b3d4d88b5145faa76960172b459bfec7d26307f164491af7b2d82bd", "24a829f24db0b2c83766c0f077fd95594aeec128c2fd3b90f5e7860b461720ef", "Components", "array,Component", GameplayConfigValueKind.Unsupported),
});
static readonly IReadOnlyList<GameplayConfigFieldDescription> s_Characters = Array.AsReadOnly(new GameplayConfigFieldDescription[] {
new GameplayConfigFieldDescription("19bc880486413a05498566725bbe35724a75286f8c000a83810bbf26cc618899", "9dffca356aef45bde5ff2e1cac236631aa16fb1d5d9d41edbc7a3ebb929afd0a", "Id", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("9cc7ccb9fee6dad5d5d6adbede625d8b5cfd487a303b11bb1d7bc79d278f6672", "ca60fdb3e55286c1b7151a3ae2da39a551e1a4a4790c2db40fe08d1323f83f63", "Attributes", "array,string", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("b9f6ea474fc417b09e85e8ffbaf3252c475c2aaaac07a4fbfec902b1a895fe56", "3877fa9dd7c52495e863494e1da0fa2dea8c574ba9dd8f44f93fe9d46bec6a4a", "InitialAttributes", "array,InitialAttribute", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("80db5eab739680fdf1617dc32cb3bc4e9f18bccd228ccd5371d1db6a85fede94", "eba6c1f86f5117831c94c964622c6b4c7ca7afcf9fac1576cd396da5a5e7ce20", "InitialTags", "array,string", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("eab12776ab905a8b4bf4ee0eb0b9dd46d3e7d8b40d8e3326972b74f377c70b29", "86474526feaabe58a51f2a450790f6e7d23c73cfad2cd513baca16c521800fa2", "Effects", "array,string", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("23c39309645f0d786e5126ff40ffbcbb4f6d6f949a120ac1dbfd7d135e49da3c", "0f214fa56dccfa0089c1ad0cc3336aae16d9120ab3356d9309491b05a88fd884", "InitialEffects", "array,InitialEffect", GameplayConfigValueKind.Unsupported),
});
public static IReadOnlyList<GameplayConfigFieldDescription> Describe(GameplayConfigRecordKind kind) => kind switch {
GameplayConfigRecordKind.Attributes => s_Attributes,
GameplayConfigRecordKind.Effects => s_Effects,
GameplayConfigRecordKind.Characters => s_Characters,
_ => throw new ArgumentOutOfRangeException(nameof(kind)) }; 
public static GameplayConfigRecordKind RecordKind(string table) => table switch {
"Attributes" => GameplayConfigRecordKind.Attributes,
"Effects" => GameplayConfigRecordKind.Effects,
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
case "60f0ac683719d2e7cadc25d66e2ff5de65928ac4c661393745b682cd031dfa3a": return GameplayConfigValue.FromInt32((int)value.Duration.Policy);
case "fa187d1262a4fa9d3307cba0f07cb3e6cfbe6072a946a9e43ab7607be4955f1a": return GameplayConfigValue.FromBoolean(value.Period.Enabled);
case "25660ad8098409fe4ca96d4fd12c3cd24aef0dee58a0d02a216055e7471b2d84": return GameplayConfigValue.FromBoolean(value.ExecuteOnApplication);
case "e832c834fd3abc22eb47f6ab685244bf1d0a09d89a37aa52dfac1d99b19b26eb": return GameplayConfigValue.FromInt32((int)value.Stacking.Policy);
case "fc32eac806c04a1d352a6c1d202efa35e44a2714d8642a7a06c35e6ee082274b": return GameplayConfigValue.FromInt32(value.Stacking.MaxStacks);
case "65260c368f9bc3e20224176ad1dbdfae055da39963b108a6be9902e84ecaa609": return GameplayConfigValue.FromInt32((int)value.Stacking.DurationUpdate);
case "84d305224d9fefd192ae3a1075123db6e84ac47cd3b4581b29b14cc28ac46504": return GameplayConfigValue.FromInt32((int)value.Stacking.PeriodUpdate);
case "1ee695c82bee907a33c4e86748d815c8edc03b63c7dfa5fe9a251581f27f6ed7": return GameplayConfigValue.FromInt32((int)value.Stacking.OverflowPolicy);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
case GameplayConfigRecordKind.Characters: {
var value = tables.Characters.Single(item => item.Id == recordId); switch (fieldId) {
case "19bc880486413a05498566725bbe35724a75286f8c000a83810bbf26cc618899": return GameplayConfigValue.FromText(value.Id);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
default: throw new ArgumentOutOfRangeException(nameof(kind)); } } } }

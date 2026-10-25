using System;
using System.Collections.Generic;
using System.Linq;
namespace ThirdPersonGameplay.Configuration { public static class GameplayConfigFields {
static readonly IReadOnlyList<GameplayConfigFieldDescription> s_Attributes = Array.AsReadOnly(new GameplayConfigFieldDescription[] {
new GameplayConfigFieldDescription("4ab2787a2957fd653fb3f890022f9389e0534215b3564051f4063b914f2df176", "a806be91a5899341178752c99f299e25e6c29eac2e5dbda7ab4ffe907c54122d", "Id", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("fe273ad5a629fcbc266162f6a2b0c95daadcd3ed8283bb9004eff08348c81c37", "2f2a3dae5d5259a2c5a5faa8060a0d51d77a35b32cd4c18898fca5b46bf37037", "AttributeId", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("8ed51ee86eb0d5081fdbbb042bc4a8ff167abf2c75875c808090fd21cadfd067", "96713a96f3443d9bbf4797465d33aaa0975197f259abc2490edfd79fc0ae42ce", "Minimum", "AttributeBound", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("c9d95dfb8cfe2f803b84ef80808465408604cbd0832191b274709c9a382df458", "5be848cc9c2fabe21ef04564fb11be665b9f5eb1abf6118e6fa6da91ba89ee53", "Maximum", "AttributeBound", GameplayConfigValueKind.Unsupported),
});
static readonly IReadOnlyList<GameplayConfigFieldDescription> s_Effects = Array.AsReadOnly(new GameplayConfigFieldDescription[] {
new GameplayConfigFieldDescription("969704024477390ad6875b93215f91001e1da6f3de1bcd60615d013018131cea", "9824989b668294422a9c7fddb842f5044e625ebc6d7093720553352be4c9509e", "Id", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("52d57c40d27a5280c8432738e5e3de0474663d07fd0ccd672066c922d1db8c0f", "27ef49854f6b49905860223cf85b55d13cc28a2437fc53f46e08ccfc05e35560", "EffectId", "string", GameplayConfigValueKind.Text),
new GameplayConfigFieldDescription("2bb4c3a3951a3214de1caca4529f5c297a9423d53fdcbaa36a7c62b361a3fded", "1c9d242d63b42c19b5a72f5f7b8c99e8b6c32d861b29c3e781a0e766351a53bf", "ScopePolicy", "ScopePolicy", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("f941acadb65e9268d3e7c8f99b9722fdecb64e536a6deeb5707572acd0fce427", "81cf6a5ed1fa5dce2202a6bcad03679bd91db7ec77eb016214e76f4abc97824b", "LifetimePolicy", "LifetimePolicy", GameplayConfigValueKind.Int32),
new GameplayConfigFieldDescription("c598ef461545031f5f36bbd0fa32e3ae1ea905ec08241c423d54228be6f630a2", "380e26df0fc3d470f67b43aaad175b39e67c298510da43f86081bdcf10ca597d", "EffectTags", "array,string", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("29b48a8da5c4404a927c6019f8d2682bc85b6538f5e55bfe71daedd592d70cb2", "83f5a0eb259e5cd2c0a381fa825ac31adbbca30a15fbfbb4b9edb4a186eb0bec", "Duration", "EffectDuration", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("0baf5abba66fbeee76f7a7e17f58ce7bf2977405d5c3b78e30db3585bb6de804", "e653276a6fc6b3b25b5555bb9c1b0b39648fc21b50e542f5ffdb121109ee99e0", "Period", "EffectPeriod", GameplayConfigValueKind.Unsupported),
new GameplayConfigFieldDescription("25660ad8098409fe4ca96d4fd12c3cd24aef0dee58a0d02a216055e7471b2d84", "842d7a068e8e9db7d58154d49692761e0db240733bfc7e6ba972a3b292c9397a", "ExecuteOnApplication", "bool", GameplayConfigValueKind.Boolean),
new GameplayConfigFieldDescription("5e1836cb9cd49e122c783d21be42c3e595b91d6c4fcf1cf548de4f84897b12f4", "9a5865e311680452fe03fcd2cc6871006f15b7d1eacd2ab2c79d43543c25513e", "Stacking", "EffectStacking", GameplayConfigValueKind.Unsupported),
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
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
case GameplayConfigRecordKind.Effects: {
var value = tables.Effects.Single(item => item.Id == recordId); switch (fieldId) {
case "969704024477390ad6875b93215f91001e1da6f3de1bcd60615d013018131cea": return GameplayConfigValue.FromText(value.Id);
case "52d57c40d27a5280c8432738e5e3de0474663d07fd0ccd672066c922d1db8c0f": return GameplayConfigValue.FromText(value.EffectId);
case "2bb4c3a3951a3214de1caca4529f5c297a9423d53fdcbaa36a7c62b361a3fded": return GameplayConfigValue.FromInt32((int)value.ScopePolicy);
case "f941acadb65e9268d3e7c8f99b9722fdecb64e536a6deeb5707572acd0fce427": return GameplayConfigValue.FromInt32((int)value.LifetimePolicy);
case "25660ad8098409fe4ca96d4fd12c3cd24aef0dee58a0d02a216055e7471b2d84": return GameplayConfigValue.FromBoolean(value.ExecuteOnApplication);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
case GameplayConfigRecordKind.Characters: {
var value = tables.Characters.Single(item => item.Id == recordId); switch (fieldId) {
case "19bc880486413a05498566725bbe35724a75286f8c000a83810bbf26cc618899": return GameplayConfigValue.FromText(value.Id);
default: throw new InvalidOperationException("记录字段不存在或不支持基础读取：" + recordId + "/" + fieldId); } }
default: throw new ArgumentOutOfRangeException(nameof(kind)); } } } }

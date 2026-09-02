using Spellkit.Compiler.Lowering;
using Spellkit.Parser;
using Spellkit.Parser.Model;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;

namespace Spellkit.Compiler;

partial class Builder
{
    private void RegisterMixin(string targetName, Qualident mixin, Location loc)
    {
        if (!types.TryGetValue(targetName, out var target))
        {
            return;
        }

        TypeInfo? mixinType = null;

        if (mixin.Parent is null)
        {
            types.TryGetValue(mixin.Local, out mixinType);
        }

        target.Mixins.Add(new(mixin.ToString(), mixinType));

        if (mixinType?.Declaration.Style == TypeDeclarationStyle.Trait)
        {
            foreach (var contract in mixinType.Declaration.Contracts)
            {
                traitRequirements.Add(new(
                    targetName,
                    mixin.ToString(),
                    GetTraitMemberName(contract),
                    loc));
            }

            return;
        }

        if (mixinType is not null || mixin.Parent is not null)
        {
            return;
        }

        foreach (var memberName in SpellkitMixinContracts.GetRequiredMembers(mixin.Local))
        {
            traitRequirements.Add(new(targetName, mixin.ToString(), memberName, loc));
        }
    }

    private void RegisterInstanceMethod(Qualident typeName, string memberName)
    {
        if (typeName.Parent is null && types.TryGetValue(typeName.Local, out var typeInfo))
        {
            typeInfo.InstanceMembers.Add(memberName);
        }
    }

    private void ValidateTraitRequirements()
    {
        var reported = new HashSet<(string Target, string Mixin, string Member)>();

        foreach (var requirement in traitRequirements)
        {
            if (!reported.Add((requirement.TargetName, requirement.MixinName, requirement.MemberName))
                || !types.TryGetValue(requirement.TargetName, out var target)
                || ProvidesInstanceMember(target, requirement.MemberName, new()))
            {
                continue;
            }

            AddError(
                CompilerError.TraitMemberNotImplemented,
                requirement.Location,
                requirement.TargetName,
                requirement.MixinName,
                Builtins.NameToOperator(requirement.MemberName));
        }
    }

    private static bool ProvidesInstanceMember(TypeInfo typeInfo, string memberName, HashSet<TypeInfo> visited)
    {
        if (!visited.Add(typeInfo))
        {
            return false;
        }

        if (typeInfo.InstanceMembers.Contains(memberName))
        {
            return true;
        }

        foreach (var mixin in typeInfo.Mixins)
        {
            if (mixin.TypeInfo is not null)
            {
                if (ProvidesInstanceMember(mixin.TypeInfo, memberName, visited))
                {
                    return true;
                }
            }
            else if (SpellkitMixinContracts.ProvidesDefaultMember(mixin.Name, memberName))
            {
                return true;
            }
        }

        return false;
    }

    private string GetTraitMemberName(LoweredFunctionDeclaration node)
    {
        var name = node.Name!;

        if (!node.IsStatic && !node.IsImplInitializer)
        {
            name = GetMethodName(name, node);
        }

        return node.Setter && !node.IsIndexer ? Builtins.Setter(name) : name;
    }
}

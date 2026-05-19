using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Секвенция концовки ветки: те же блоки, что у <see cref="GameRestartSequenceController"/>.
/// Стартует из <see cref="GameManager"/> после окончания ролика финальной ноды.
/// Специфичные блоки концовки добавляются в <see cref="TryProcessInstantBlock"/> позже.
/// </summary>
[DisallowMultipleComponent]
public sealed class BranchEndingSequenceController : MapSequenceControllerBase
{
    [Tooltip("Порядок блоков (титры, отмотка, fade — настраивается в инспекторе).")]
    [SerializeField]
    private List<GameRestartSequenceBlock> blocks = new List<GameRestartSequenceBlock>();

    private Node _branchEndNode;

    protected override IReadOnlyList<GameRestartSequenceBlock> SequenceBlocks => blocks;

    /// <summary>Нода ветки, чей ролик только что закончился (до конца секвенции).</summary>
    public Node BranchEndNode => _branchEndNode;

    /// <summary>Запуск секвенции концовки для завершённой ветки.</summary>
    public void PlayForBranchEnd(Node finalNode)
    {
        if (!Application.isPlaying || finalNode == null)
            return;
        if (IsPlaying)
            return;

        _branchEndNode = finalNode;
        Play();
    }

    protected override void OnSequenceFinished()
    {
        _branchEndNode = null;
    }

    protected override void OnSequenceStopped()
    {
        _branchEndNode = null;
    }

    protected override bool TryProcessInstantBlock(GameRestartSequenceBlock block, int blockIndex)
    {
        // Специфичные блоки концовки (титры и т.д.) — добавить сюда.
        return false;
    }
}

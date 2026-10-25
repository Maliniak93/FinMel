import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
  output,
  signal,
  type ResourceRef,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  putApiPortfolioAssetGroupsAssignment,
  type AssetGroupResponse,
  type AssignableAssetResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { groupColor } from '../../../shared/colors';
import { Skeleton } from '../../../shared/skeleton/skeleton';
import { ASSET_CLASSES, assetClassLabel } from '../../assets/asset-class';

@Component({
  selector: 'app-group-assets-panel',
  imports: [
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    Skeleton,
    TranslocoPipe,
  ],
  templateUrl: './group-assets-panel.html',
  styleUrl: './group-assets-panel.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupAssetsPanel {
  private readonly snackBar = inject(MatSnackBar);

  readonly groupId = input.required<string | null>();
  readonly groups = input.required<AssetGroupResponse[]>();
  readonly assetsResource = input.required<ResourceRef<AssignableAssetResponse[] | undefined>>();

  readonly closed = output<void>();
  readonly saved = output<boolean>();

  protected readonly saving = signal(false);

  protected readonly textFilter = linkedSignal({ source: this.groupId, computation: () => '' });
  protected readonly classFilter = linkedSignal<string | null, number | null>({
    source: this.groupId,
    computation: () => null,
  });

  protected readonly readOnly = computed(() => this.groupId() === null);
  protected readonly group = computed(
    () => this.groups().find((group) => group.id === this.groupId()) ?? null,
  );

  private readonly allAssets = computed(() => {
    const resource = this.assetsResource();
    return resource.hasValue() ? (resource.value() ?? []) : [];
  });

  private readonly listedAssets = computed(() =>
    this.readOnly() ? this.allAssets().filter((asset) => asset.groupId === null) : this.allAssets(),
  );

  private readonly members = computed(
    () =>
      new Set(
        this.allAssets()
          .filter((asset) => asset.groupId === this.groupId())
          .map((asset) => asset.assetId),
      ),
  );

  protected readonly checked = linkedSignal<ReadonlySet<string>>(() => this.members());

  protected readonly presentClasses = computed(() => {
    const present = new Set(this.listedAssets().map((asset) => Number(asset.assetClass)));
    return ASSET_CLASSES.filter((assetClass) => present.has(Number(assetClass.value)));
  });

  protected readonly shownAssets = computed(() => {
    const text = this.textFilter().trim().toLowerCase();
    const assetClass = this.classFilter();
    return this.listedAssets().filter(
      (asset) =>
        (assetClass === null || Number(asset.assetClass) === assetClass) &&
        (text === '' ||
          [asset.name, asset.accountName, asset.cashPlaceName].some((value) =>
            (value ?? '').toLowerCase().includes(text),
          )),
    );
  });

  protected readonly changeCount = computed(() => {
    const checked = this.checked();
    const members = this.members();
    return (
      [...checked].filter((id) => !members.has(id)).length +
      [...members].filter((id) => !checked.has(id)).length
    );
  });

  protected readonly hasAssets = computed(() => this.listedAssets().length > 0);

  protected readonly assetClassLabel = assetClassLabel;

  protected groupName(groupId: string | null): string {
    return this.groups().find((group) => group.id === groupId)?.name ?? '';
  }

  protected groupColorOf(groupId: string | null): string {
    return groupColor(this.groups().find((group) => group.id === groupId)?.color);
  }

  protected onFilterInput(event: Event): void {
    this.textFilter.set((event.target as HTMLInputElement).value);
  }

  protected toggle(assetId: string, isChecked: boolean): void {
    this.checked.update((current) => {
      const next = new Set(current);
      if (isChecked) {
        next.add(assetId);
      } else {
        next.delete(assetId);
      }
      return next;
    });
  }

  protected selectAllShown(): void {
    this.checked.update((current) => {
      const next = new Set(current);
      this.shownAssets().forEach((asset) => next.add(asset.assetId));
      return next;
    });
  }

  protected async saveAssignment(): Promise<void> {
    const groupId = this.groupId();
    if (groupId === null || this.saving() || this.changeCount() === 0) {
      return;
    }

    const checked = this.checked();
    const members = this.members();
    const added = [...checked].filter((id) => !members.has(id));
    const removed = [...members].filter((id) => !checked.has(id));

    let complete = true;
    this.saving.set(true);
    try {
      for (const [assetIds, target] of [
        [added, groupId],
        [removed, null],
      ] as const) {
        if (assetIds.length === 0) {
          continue;
        }

        const result = await putApiPortfolioAssetGroupsAssignment({
          body: { assetIds: [...assetIds], groupId: target },
        });
        if (result.error) {
          this.snackBar.open(
            readProblemDetails(result.error).detail ?? translate('groups.assets.saveFailed'),
            translate('common.dismiss'),
          );
          complete = false;
          break;
        }
      }
    } finally {
      this.saving.set(false);
    }

    this.saved.emit(complete);
  }
}

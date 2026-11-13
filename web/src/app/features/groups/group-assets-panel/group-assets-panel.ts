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
import { MatPaginatorModule, type PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  putApiPortfolioAssetGroupsByGroupIdMembers,
  type AssetGroupResponse,
  type AssetGroupTreeItem,
  type AssignableAssetResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { groupColor } from '../../../shared/colors';
import { Skeleton } from '../../../shared/skeleton/skeleton';
import { ASSET_CLASSES, assetClassLabel } from '../../assets/asset-class';
import {
  accountRow,
  assetRow,
  childRow,
  flattenVisible,
  page,
  rowState,
  type TreeRow,
} from '../group-tree/group-tree';

const PAGE_SIZE = 20;

interface LoadState {
  error(): Error | undefined;
  isLoading(): boolean;
  hasValue(): boolean;
  reload(): boolean;
}

@Component({
  selector: 'app-group-assets-panel',
  imports: [
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatSelectModule,
    MatTooltipModule,
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
  readonly treeResource = input.required<ResourceRef<AssetGroupTreeItem[] | undefined>>();

  readonly closed = output<void>();
  readonly saved = output<boolean>();

  protected readonly pageSize = PAGE_SIZE;
  protected readonly saving = signal(false);
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());
  protected readonly pageIndex = linkedSignal<string | null, number>({
    source: this.groupId,
    computation: () => 0,
  });

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
    this.allAssets().filter((asset) => asset.groupId === null),
  );

  private readonly tree = computed(() => {
    const resource = this.treeResource();
    return resource.hasValue() ? (resource.value() ?? []) : [];
  });

  private readonly treeClasses = computed(() => {
    const present = new Set<number>();
    for (const item of this.tree()) {
      if (item.assetClass !== null) {
        present.add(Number(item.assetClass));
      }
      item.children.forEach((child) => present.add(Number(child.assetClass)));
    }
    return present;
  });

  protected readonly presentClasses = computed(() => {
    const present = this.readOnly()
      ? new Set(this.listedAssets().map((asset) => Number(asset.assetClass)))
      : this.treeClasses();
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

  private readonly overrides = linkedSignal<
    { groupId: string | null; tree: AssetGroupTreeItem[] },
    ReadonlyMap<string, boolean>
  >({
    source: () => ({ groupId: this.groupId(), tree: this.tree() }),
    computation: () => new Map(),
  });

  protected readonly source = computed<LoadState>(() =>
    this.readOnly() ? this.assetsResource() : this.treeResource(),
  );

  protected readonly rows = computed(() =>
    flattenVisible(this.tree(), this.expanded(), {
      text: this.textFilter(),
      assetClass: this.classFilter(),
    }),
  );

  protected readonly pageRows = computed(() => page(this.rows(), this.pageIndex(), PAGE_SIZE));

  protected readonly changeCount = computed(() => {
    const groupId = this.groupId();
    const overrides = this.overrides();
    if (groupId === null || overrides.size === 0) {
      return 0;
    }
    return this.allRows().filter((row) => this.isChecked(row) !== rowState(row, groupId).checked)
      .length;
  });

  protected readonly hasAssets = computed(() =>
    this.readOnly() ? this.listedAssets().length > 0 : this.tree().length > 0,
  );

  protected readonly assetClassLabel = assetClassLabel;

  private allRows(): TreeRow[] {
    return this.tree().flatMap((item) =>
      item.kind === 'Account'
        ? [
            accountRow(item, false, false),
            ...item.children.map((child) => childRow(item.accountId!, child)),
          ]
        : [assetRow(item)],
    );
  }

  protected groupName(groupId: string | null): string {
    return this.groups().find((group) => group.id === groupId)?.name ?? '';
  }

  protected groupColorOf(groupId: string | null): string {
    return groupColor(this.groups().find((group) => group.id === groupId)?.color);
  }

  protected onFilterInput(event: Event): void {
    this.textFilter.set((event.target as HTMLInputElement).value);
    this.pageIndex.set(0);
  }

  protected onClassFilter(assetClass: number | null): void {
    this.classFilter.set(assetClass);
    this.pageIndex.set(0);
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
  }

  protected toggleLabel(row: TreeRow): string {
    return row.expanded ? 'groups.assets.collapse' : 'groups.assets.expand';
  }

  protected toggleExpanded(key: string): void {
    this.expanded.update((current) => {
      const next = new Set(current);
      if (!next.delete(key)) {
        next.add(key);
      }
      return next;
    });
  }

  protected isChecked(row: TreeRow): boolean {
    const overrides = this.overrides();
    const own = overrides.get(row.key);
    if (own !== undefined) {
      return own;
    }
    const inherited =
      row.accountKey === null || row.isException ? undefined : overrides.get(row.accountKey);
    return inherited ?? rowState(row, this.groupId()).checked;
  }

  protected isGreyed(row: TreeRow): boolean {
    return rowState(row, this.groupId()).greyed && !this.isChecked(row);
  }

  protected chipOf(row: TreeRow): string | null {
    return this.isGreyed(row) ? rowState(row, this.groupId()).chip : null;
  }

  protected toggle(row: TreeRow, isChecked: boolean): void {
    this.overrides.update((current) => {
      const next = new Map(current);
      if (row.kind === 'account') {
        this.tree()
          .find((item) => item.accountId === row.key)
          ?.children.filter((child) => !child.isException)
          .forEach((child) => next.delete(child.assetId));
      }
      next.set(row.key, isChecked);
      return next;
    });
  }

  protected selectAllShown(): void {
    this.rows()
      .filter((row) => !rowState(row, this.groupId()).greyed)
      .forEach((row) => this.toggle(row, true));
  }

  protected async saveAssignment(): Promise<void> {
    const groupId = this.groupId();
    if (groupId === null || this.saving() || this.changeCount() === 0) {
      return;
    }

    const rows = this.allRows();
    const checked = rows.filter((row) => this.isChecked(row));

    this.saving.set(true);
    let complete = true;
    try {
      const result = await putApiPortfolioAssetGroupsByGroupIdMembers({
        path: { groupId },
        body: {
          accountIds: checked.filter((row) => row.kind === 'account').map((row) => row.key),
          assetIds: checked.filter((row) => row.kind !== 'account').map((row) => row.key),
        },
      });
      if (result.error) {
        this.snackBar.open(
          readProblemDetails(result.error).detail ?? translate('groups.assets.saveFailed'),
          translate('common.dismiss'),
        );
        complete = false;
      }
    } finally {
      this.saving.set(false);
    }

    this.saved.emit(complete);
  }
}

import { ComponentFixture, TestBed } from '@angular/core/testing';

import { provideI18nTesting } from '../../core/i18n/testing';
import { GROUP_KIND } from '../../features/groups/group-kind';
import { GroupKindMarker } from './group-kind-marker';

describe('GroupKindMarker', () => {
  let fixture: ComponentFixture<GroupKindMarker>;

  async function render(kind: number): Promise<HTMLElement> {
    await TestBed.configureTestingModule({
      imports: [GroupKindMarker],
      providers: [provideI18nTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(GroupKindMarker);
    fixture.componentRef.setInput('kind', kind);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows a shield and the word for a cushion', async () => {
    const root = await render(GROUP_KIND.Cushion);

    expect(root.querySelector('mat-icon')?.textContent?.trim()).toBe('shield');
    expect(root.textContent).toContain('Cushion');
  });

  it('shows only the word for a regular group', async () => {
    const root = await render(GROUP_KIND.Regular);

    expect(root.querySelector('mat-icon')).toBeNull();
    expect(root.textContent).toContain('Regular');
  });
});

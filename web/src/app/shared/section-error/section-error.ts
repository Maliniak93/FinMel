import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe } from '@jsverse/transloco';

@Component({
  selector: 'app-section-error',
  imports: [MatButtonModule, TranslocoPipe],
  templateUrl: './section-error.html',
  styleUrl: './section-error.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'alert' },
})
export class SectionError {
  readonly message = input<string>();
  readonly retry = output<void>();
}

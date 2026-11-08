import { Component, input } from '@angular/core';

@Component({
  selector: 'app-collapse',
  templateUrl: './collapse.html',
  styleUrl: './collapse.scss',
  host: {
    '[class.collapse--open]': 'open()',
    '[attr.inert]': "open() ? null : ''",
  },
})
export class Collapse {
  readonly open = input(false);
}

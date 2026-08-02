import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Portfolios } from './portfolios';

describe('Portfolios', () => {
  let component: Portfolios;
  let fixture: ComponentFixture<Portfolios>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Portfolios],
    }).compileComponents();

    fixture = TestBed.createComponent(Portfolios);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
